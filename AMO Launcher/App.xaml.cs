using AMO_Launcher.Services;
using AMO_Launcher.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AMO_Launcher
{
    public partial class App : Application
    {
        // Services that will be accessible throughout the application
        public static GameDetectionService GameDetectionService { get; private set; } = null!;
        public static ConfigurationService ConfigService { get; private set; } = null!;
        public static IconCacheService IconCacheService { get; private set; } = null!;
        public static ManualGameIconService ManualGameIconService { get; private set; } = null!;
        public static ModDetectionService ModDetectionService { get; private set; } = null!;
        public static GameBackupService GameBackupService { get; private set; } = null!;
        public static ProfileService ProfileService { get; private set; } = null!;
        public static UpdateService UpdateService { get; private set; } = null!;
        public static LogService LogService { get; private set; }

        // Log file for debugging
        private static readonly string LogFilePath;

        // GitHub repository info for updates
        private const string GITHUB_OWNER = "KolarF1";
        private const string GITHUB_REPO = "AMO-Launcher";

        // For tracking application lifetime
        private static Stopwatch _appLifetimeStopwatch;

        // Enumeration of error categories for better organization
        public enum ErrorCategory
        {
            FileSystem,     // File and directory access errors
            Network,        // Network and connectivity issues
            ModProcessing,  // Errors during mod processing
            GameExecution,  // Game launch and execution errors
            Configuration,  // Configuration and settings errors
            UI,             // User interface errors
            Services,       // Service initialization or operation errors
            Startup,        // Application startup errors
            Shutdown,       // Application shutdown errors
            Unknown         // Uncategorized errors
        }

        // Static constructor to initialize the log file path
        static App()
        {
            // Use the same AppData folder that ConfigurationService uses
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            // Create directory if it doesn't exist
            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch
                {
                    // If we can't create the directory, fall back to desktop
                    LogFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "AMO_Launcher_Debug.log");
                    return;
                }
            }

            // Set the log file path to the AppData location
            LogFilePath = Path.Combine(appDataPath, "AMO_Launcher_Debug.log");

            // Initialize app lifetime stopwatch
            _appLifetimeStopwatch = Stopwatch.StartNew();
        }

        public App()
        {
            // Hook up the unhandled exception handler
            this.DispatcherUnhandledException += Application_DispatcherUnhandledException;

            // Start with a clean log
            if (File.Exists(LogFilePath))
            {
                try { File.Delete(LogFilePath); } catch { /* Ignore if we can't delete the file */ }
            }

            // Log application start with basic system info
            LogToFile("=== APPLICATION STARTING ===");
            LogToFile($"Date/Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogToFile($"OS: {Environment.OSVersion}");
            LogToFile($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            LogToFile($"64-bit Process: {Environment.Is64BitProcess}");
            LogToFile($".NET Version: {Environment.Version}");
            LogToFile($"Machine Name: {Environment.MachineName}");
            LogToFile($"Processor Count: {Environment.ProcessorCount}");
            LogToFile($"Working Set: {Environment.WorkingSet / 1024 / 1024} MB");
            LogToFile("=============================");
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Create a context for the entire startup operation
            string startupContextId = $"Startup_{DateTime.Now:yyyyMMdd_HHmmss}";
            LogToFile($"[{startupContextId}] Application startup sequence initiated");

            try
            {
                base.OnStartup(e);
                LogToFile($"[{startupContextId}] Base.OnStartup called");

                // Initialize application services with context tracking
                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    LogToFile($"[{startupContextId}] Initializing services");
                    await InitializeServicesAsync();
                    LogToFile($"[{startupContextId}] Services initialized successfully");
                }, "Service initialization", showErrorToUser: true);

                // Load settings with error handling
                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    LogToFile($"[{startupContextId}] Loading application settings");
                    await ConfigService.LoadSettingsAsync();
                    LogToFile($"[{startupContextId}] Settings loaded successfully");
                }, "Loading settings", showErrorToUser: true);

                // Apply low usage mode settings if enabled
                ErrorHandler.ExecuteSafe(() =>
                {
                    LogToFile($"[{startupContextId}] Checking low usage mode");
                    ApplyLowUsageMode();
                }, "Apply low usage mode", showErrorToUser: false);

                // Create and show the main window
                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    LogToFile($"[{startupContextId}] Creating MainWindow");

                    // Track the window creation performance
                    var windowStopwatch = Stopwatch.StartNew();
                    var mainWindow = new MainWindow();
                    windowStopwatch.Stop();

                    LogToFile($"[{startupContextId}] MainWindow created in {windowStopwatch.ElapsedMilliseconds}ms");

                    // Track window show performance
                    windowStopwatch.Restart();
                    mainWindow.Show();
                    windowStopwatch.Stop();

                    LogToFile($"[{startupContextId}] MainWindow shown in {windowStopwatch.ElapsedMilliseconds}ms");

                    // Now log with the proper LogService if available
                    if (LogService != null)
                    {
                        LogService.Info($"Application startup completed in {_appLifetimeStopwatch.ElapsedMilliseconds}ms");
                    }
                }, "MainWindow creation", showErrorToUser: true);

                // Check for updates in the background after startup based on settings
                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    if (ConfigService.GetAutoCheckForUpdatesAtStartup())
                    {
                        LogToFile($"[{startupContextId}] Auto-checking for updates in the background");
                        await CheckForUpdatesAsync(true);
                    }
                    else
                    {
                        LogToFile($"[{startupContextId}] Auto-updates disabled, skipping update check");
                    }
                }, "Update check", showErrorToUser: false);
            }
            catch (Exception ex)
            {
                LogCategorizedError("FATAL ERROR during startup", ex, ErrorCategory.Startup, startupContextId);
                MessageBox.Show($"Fatal error during startup: {ex.Message}\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LogToFile($"[{startupContextId}] Startup sequence completed");
            }
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            // Use a context ID to track this specific update check
            string updateContextId = $"UpdateCheck_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                if (LogService != null)
                {
                    LogService.LogDebug($"[{updateContextId}] Starting update check (silent={silent})");
                }

                // Track the performance of the update check
                var updateStopwatch = Stopwatch.StartNew();

                await UpdateService.CheckForUpdatesAsync(silent);

                updateStopwatch.Stop();

                if (LogService != null)
                {
                    LogService.LogDebug($"[{updateContextId}] Update check completed in {updateStopwatch.ElapsedMilliseconds}ms");

                    // Log a warning if the update check was slow
                    if (updateStopwatch.ElapsedMilliseconds > 5000)
                    {
                        LogService.Warning($"[{updateContextId}] Update check took longer than expected ({updateStopwatch.ElapsedMilliseconds}ms)");
                    }
                }
            }
            catch (Exception ex)
            {
                LogCategorizedError("Error checking for updates", ex, ErrorCategory.Network, updateContextId);
            }
        }

        private async Task InitializeServicesAsync()
        {
            // Create a context for the service initialization
            string servicesContextId = $"Services_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                LogToFile($"[{servicesContextId}] Starting services initialization");

                var sw = Stopwatch.StartNew();

                // Initialize ConfigService first (other services depend on it)
                LogToFile($"[{servicesContextId}] Initializing ConfigService");
                ConfigService = new ConfigurationService();
                LogToFile($"[{servicesContextId}] ConfigService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize LogService early for better logging
                sw.Restart();
                LogToFile($"[{servicesContextId}] Initializing LogService");
                LogService = new LogService();
                LogService.Initialize(ConfigService);
                LogToFile($"[{servicesContextId}] LogService initialized in {sw.ElapsedMilliseconds}ms");

                // Now that we have LogService, use it for the rest of initialization
                LogService.Info($"[{servicesContextId}] Continuing service initialization with LogService");

                // Initialize IconCacheService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing IconCacheService");
                IconCacheService = new IconCacheService();
                LogService.LogDebug($"[{servicesContextId}] IconCacheService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize ManualGameIconService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ManualGameIconService");
                ManualGameIconService = new ManualGameIconService();
                LogService.LogDebug($"[{servicesContextId}] ManualGameIconService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize GameDetectionService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing GameDetectionService");
                GameDetectionService = new GameDetectionService();
                LogService.LogDebug($"[{servicesContextId}] GameDetectionService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize ModDetectionService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ModDetectionService");
                ModDetectionService = new ModDetectionService();
                LogService.LogDebug($"[{servicesContextId}] ModDetectionService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize GameBackupService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing GameBackupService");
                GameBackupService = new GameBackupService();
                LogService.LogDebug($"[{servicesContextId}] GameBackupService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize ProfileService (after ConfigService)
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ProfileService");
                ProfileService = new ProfileService(ConfigService);
                LogService.LogDebug($"[{servicesContextId}] ProfileService initialized in {sw.ElapsedMilliseconds}ms");

                // Initialize UpdateService
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing UpdateService");
                UpdateService = new UpdateService(GITHUB_OWNER, GITHUB_REPO);
                LogService.LogDebug($"[{servicesContextId}] UpdateService initialized in {sw.ElapsedMilliseconds}ms");

                // Load profiles and perform maintenance
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Loading profiles");
                await ProfileService.LoadProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profiles loaded in {sw.ElapsedMilliseconds}ms");

                // Run deduplication to clean up any existing duplicate profiles
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Deduplicating profiles");
                await ProfileService.DeduplicateProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profile deduplication completed in {sw.ElapsedMilliseconds}ms");

                // Normalize all game IDs to ensure consistency
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Migrating game profiles");
                await ProfileService.MigrateGameProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profile migration completed in {sw.ElapsedMilliseconds}ms");

                // Save all changes to ensure profiles are consistent
                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Saving profiles");
                await ProfileService.SaveProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profiles saved in {sw.ElapsedMilliseconds}ms");

                // Log successful initialization
                LogService.Info($"[{servicesContextId}] All services initialized successfully");
            }
            catch (Exception ex)
            {
                LogCategorizedError("ERROR in InitializeServices", ex, ErrorCategory.Services, servicesContextId);

                // Handle any initialization errors
                MessageBox.Show($"Error initializing services: {ex.Message}\n\nThe application may not function correctly.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Initialize with fallback empty services if needed
                InitializeFallbackServices();
            }
        }

        private void InitializeFallbackServices()
        {
            // Create a context for the fallback initialization
            string fallbackContextId = $"Fallback_{DateTime.Now:yyyyMMdd_HHmmss}";

            LogToFile($"[{fallbackContextId}] Initializing fallback services due to initialization failure");

            if (ConfigService == null)
            {
                LogToFile($"[{fallbackContextId}] Creating fallback ConfigService");
                ConfigService = new ConfigurationService();
            }

            if (LogService == null)
            {
                LogToFile($"[{fallbackContextId}] Creating fallback LogService");
                LogService = new LogService();

                try
                {
                    LogService.Initialize(ConfigService);
                }
                catch
                {
                    LogToFile($"[{fallbackContextId}] Failed to initialize fallback LogService");
                }
            }

            if (IconCacheService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback IconCacheService");
                IconCacheService = new IconCacheService();
            }

            if (ManualGameIconService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback ManualGameIconService");
                ManualGameIconService = new ManualGameIconService();
            }

            if (GameDetectionService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback GameDetectionService");
                GameDetectionService = new GameDetectionService();
            }

            if (ModDetectionService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback ModDetectionService");
                ModDetectionService = new ModDetectionService();
            }

            if (GameBackupService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback GameBackupService");
                GameBackupService = new GameBackupService();
            }

            if (ProfileService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback ProfileService");
                ProfileService = new ProfileService(ConfigService);
            }

            if (UpdateService == null)
            {
                LogService?.LogDebug($"[{fallbackContextId}] Creating fallback UpdateService");
                UpdateService = new UpdateService(GITHUB_OWNER, GITHUB_REPO);
            }

            LogService?.Info($"[{fallbackContextId}] Fallback services initialization completed");
        }

        // Apply Low Usage Mode settings
        private void ApplyLowUsageMode()
        {
            // Track resource impact
            Stopwatch lowUsageSw = Stopwatch.StartNew();

            if (ConfigService.GetLowUsageMode())
            {
                if (LogService != null)
                {
                    LogService.Info("Applying Low Usage Mode settings");
                }
                else
                {
                    LogToFile("Applying Low Usage Mode settings");
                }

                // Set GC settings to minimize memory usage
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                // Disable animations if applicable
                // Example:
                /*
                if (Current.Resources.Contains("EnableAnimations"))
                {
                    Current.Resources["EnableAnimations"] = false;
                }
                */

                // Set rendering tier to lower quality
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

                lowUsageSw.Stop();

                if (LogService != null)
                {
                    LogService.LogDebug($"Low Usage Mode applied in {lowUsageSw.ElapsedMilliseconds}ms");

                    // Log memory usage after applying low usage mode
                    LogService.LogDebug($"Working set after Low Usage Mode: {Environment.WorkingSet / 1024 / 1024} MB");
                }
            }
        }

        // Log to a file in AppData/Roaming/AMO_Launcher folder
        public static void LogToFile(string message, bool isDetailedLog = false)
        {
            try
            {
                if (LogService == null)
                {
                    // Fall back to direct file logging if LogService is not initialized
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logMessage = $"[{timestamp}] {message}";
                    File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
                    return;
                }

                if (isDetailedLog)
                {
                    // Detailed logs go to Debug level
                    LogService.LogDebug(message);
                }
                else
                {
                    // Standard logs go to Info level
                    LogService.Info(message);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        // Overload for detailed logging
        public static void LogDetailedToFile(string message)
        {
            if (LogService != null)
            {
                LogService.LogDebug(message);
            }
            else
            {
                LogToFile(message, true);
            }
        }

        // Enhanced error logging with categorization and context tracking
        public static void LogCategorizedError(string message, Exception ex, ErrorCategory category, string contextId = null)
        {
            string categoryStr = $"[{category}]";
            string contextStr = !string.IsNullOrEmpty(contextId) ? $"[{contextId}] " : "";
            string fullMessage = $"{contextStr}{categoryStr} {message}";

            if (LogService == null)
            {
                string exceptionDetails = ex != null ? $" - Exception: {ex.Message}" : "";
                LogToFile($"ERROR: {fullMessage}{exceptionDetails}");

                if (ex != null)
                {
                    LogToFile($"Stack trace: {ex.StackTrace}", true);

                    // Log inner exception details
                    if (ex.InnerException != null)
                    {
                        LogToFile($"Inner exception: {ex.InnerException.Message} - {ex.InnerException.StackTrace}", true);
                    }
                }

                return;
            }

            // Log with proper categorization
            LogService.Error(fullMessage);

            if (ex != null)
            {
                // Log detailed exception information
                LogService.LogDebug($"{contextStr}{categoryStr} Exception type: {ex.GetType().Name}");
                LogService.LogDebug($"{contextStr}{categoryStr} Stack trace: {ex.StackTrace}");

                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    LogService.LogDebug($"{contextStr}{categoryStr} Inner exception: {ex.InnerException.Message}");
                    LogService.LogDebug($"{contextStr}{categoryStr} Inner stack trace: {ex.InnerException.StackTrace}");
                }

                // Category-specific additional logging
                switch (category)
                {
                    case ErrorCategory.FileSystem:
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            LogService.LogDebug($"{contextStr}{categoryStr} File operation failed - check permissions and if file is in use");
                        }
                        break;

                    case ErrorCategory.Network:
                        if (ex is System.Net.WebException webEx)
                        {
                            LogService.LogDebug($"{contextStr}{categoryStr} Network status: {webEx.Status}");

                            if (webEx.Response != null)
                            {
                                var response = webEx.Response as System.Net.HttpWebResponse;
                                LogService.LogDebug($"{contextStr}{categoryStr} HTTP Status: {response?.StatusCode} - {response?.StatusDescription}");
                            }
                        }
                        break;

                    case ErrorCategory.ModProcessing:
                        // For mod errors, check if it's a mod format issue
                        if (ex.Message.Contains("json") || ex.Message.Contains("JSON"))
                        {
                            LogService.LogDebug($"{contextStr}{categoryStr} Possible mod.json parsing error - invalid format");
                        }
                        else if (ex.Message.Contains("read") || ex.Message.Contains("extract"))
                        {
                            LogService.LogDebug($"{contextStr}{categoryStr} Possible mod file extraction error");
                        }
                        break;
                }
            }
        }

        // Standard error logging for backward compatibility
        public static void LogError(string message, Exception ex = null)
        {
            LogCategorizedError(message, ex, ErrorCategory.Unknown);
        }

        // Warning logging
        public static void LogWarning(string message)
        {
            if (LogService != null)
            {
                LogService.Warning(message);
            }
            else
            {
                LogToFile($"WARNING: {message}");
            }
        }

        // Trace logging for detailed diagnostics
        public static void LogTrace(string message)
        {
            if (LogService != null)
            {
                LogService.Trace(message);
            }
            else
            {
                LogToFile(message, true);
            }
        }

        // Performance tracking helper
        public static void LogPerformance(string operation, long elapsedMilliseconds, long warningThresholdMs = 1000)
        {
            if (LogService == null)
            {
                LogToFile($"PERF: {operation} took {elapsedMilliseconds}ms");
                return;
            }

            if (elapsedMilliseconds > warningThresholdMs)
            {
                LogService.Warning($"PERF: {operation} took {elapsedMilliseconds}ms (exceeded {warningThresholdMs}ms threshold)");
            }
            else
            {
                LogService.LogDebug($"PERF: {operation} took {elapsedMilliseconds}ms");
            }
        }

        // Handle unhandled exceptions to prevent crashes
        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string exceptionContextId = $"UnhandledException_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Analyze the stack trace to determine the error category
            ErrorCategory category = DetermineErrorCategory(e.Exception);

            LogCategorizedError("UNHANDLED EXCEPTION", e.Exception, category, exceptionContextId);

            MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nPlease restart the application.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Create a mini dump for severe errors if possible
            if (category == ErrorCategory.Startup || category == ErrorCategory.Services)
            {
                try
                {
                    CreateMiniDump(exceptionContextId);
                }
                catch
                {
                    // Ignore errors in creating dump
                }
            }

            // Mark as handled to prevent application crash
            e.Handled = true;
        }

        // Try to determine the error category from the exception
        private ErrorCategory DetermineErrorCategory(Exception ex)
        {
            if (ex == null) return ErrorCategory.Unknown;

            // Check exception type
            if (ex is IOException || ex is UnauthorizedAccessException || ex is FileNotFoundException)
                return ErrorCategory.FileSystem;

            if (ex is System.Net.WebException || ex is System.Net.Sockets.SocketException)
                return ErrorCategory.Network;

            // Check the stack trace for hints
            string stackTrace = ex.StackTrace ?? "";

            if (stackTrace.Contains("GameDetection") || stackTrace.Contains("GameExecution"))
                return ErrorCategory.GameExecution;

            if (stackTrace.Contains("ModDetection") || stackTrace.Contains("ModProcessing"))
                return ErrorCategory.ModProcessing;

            if (stackTrace.Contains("ConfigurationService") || stackTrace.Contains("Settings"))
                return ErrorCategory.Configuration;

            if (stackTrace.Contains("MainWindow") || stackTrace.Contains("UI") ||
                stackTrace.Contains("View") || stackTrace.Contains("Page"))
                return ErrorCategory.UI;

            if (ex.Message.Contains("service") || stackTrace.Contains("Service"))
                return ErrorCategory.Services;

            // Check if this is a startup error
            if (_appLifetimeStopwatch.ElapsedMilliseconds < 10000) // Within first 10 seconds
                return ErrorCategory.Startup;

            return ErrorCategory.Unknown;
        }

        // Create a mini dump for severe errors
        private void CreateMiniDump(string contextId)
        {
            if (LogService != null)
            {
                LogService.LogDebug($"[{contextId}] Creating mini dump for debugging");
            }

            string dumpPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher",
                $"CrashDump_{DateTime.Now:yyyyMMdd_HHmmss}.dmp");

            // In a real implementation, you would use the Windows API to create a mini dump
            // For this example, we'll just log that we would create one
            if (LogService != null)
            {
                LogService.LogDebug($"[{contextId}] Mini dump would be created at: {dumpPath}");
            }
            else
            {
                LogToFile($"[{contextId}] Mini dump would be created at: {dumpPath}");
            }
        }

        // Application shutdown tracking
        protected override void OnExit(ExitEventArgs e)
        {
            string shutdownContextId = $"Shutdown_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                base.OnExit(e);

                // Stop the app lifetime stopwatch
                _appLifetimeStopwatch.Stop();

                if (LogService != null)
                {
                    LogService.Info($"[{shutdownContextId}] Application exiting with code: {e.ApplicationExitCode}");
                    LogService.Info($"[{shutdownContextId}] Total runtime: {_appLifetimeStopwatch.ElapsedMilliseconds}ms");

                    // Log any pending flush operations
                    LogService.LogDebug($"[{shutdownContextId}] Performing final logging flush");
                }
                else
                {
                    LogToFile($"[{shutdownContextId}] Application exiting with code: {e.ApplicationExitCode}");
                }
            }
            catch (Exception ex)
            {
                // Last resort logging
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string message = $"[{timestamp}] [{shutdownContextId}] Error during shutdown: {ex.Message}";
                    File.AppendAllText(LogFilePath, message + Environment.NewLine);
                }
                catch
                {
                    // Nothing we can do if this fails
                }
            }
        }
    }
}
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
        public static GameDetectionService GameDetectionService { get; private set; } = null!;
        public static ConfigurationService ConfigService { get; private set; } = null!;
        public static IconCacheService IconCacheService { get; private set; } = null!;
        public static ManualGameIconService ManualGameIconService { get; private set; } = null!;
        public static ModDetectionService ModDetectionService { get; private set; } = null!;
        public static GameBackupService GameBackupService { get; private set; } = null!;
        public static ProfileService ProfileService { get; private set; } = null!;
        public static UpdateService UpdateService { get; private set; } = null!;
        public static LogService LogService { get; private set; }

        private static readonly string LogFilePath;

        private const string GITHUB_OWNER = "KolarF1";
        private const string GITHUB_REPO = "AMO-Launcher";

        private static Stopwatch _appLifetimeStopwatch;

        public enum ErrorCategory
        {
            FileSystem,
            Network,
            ModProcessing,
            GameExecution,
            Configuration,
            UI,
            Services,
            Startup,
            Shutdown,
            Unknown
        }

        static App()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch
                {
                    LogFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "AMO_Launcher_Debug.log");
                    return;
                }
            }

            LogFilePath = Path.Combine(appDataPath, "AMO_Launcher_Debug.log");

            _appLifetimeStopwatch = Stopwatch.StartNew();
        }

        public App()
        {
            this.DispatcherUnhandledException += Application_DispatcherUnhandledException;

            if (File.Exists(LogFilePath))
            {
                try { File.Delete(LogFilePath); } catch { }
            }

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
            string startupContextId = $"Startup_{DateTime.Now:yyyyMMdd_HHmmss}";
            LogToFile($"[{startupContextId}] Application startup sequence initiated");

            try
            {
                base.OnStartup(e);
                LogToFile($"[{startupContextId}] Base.OnStartup called");

                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    LogToFile($"[{startupContextId}] Initializing services");
                    await InitializeServicesAsync();
                    LogToFile($"[{startupContextId}] Services initialized successfully");
                }, "Service initialization", showErrorToUser: true);

                ErrorHandler.ExecuteSafe(() =>
                {
                    LogToFile($"[{startupContextId}] Checking low usage mode");
                    ApplyLowUsageMode();
                }, "Apply low usage mode", showErrorToUser: false);

                await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    LogToFile($"[{startupContextId}] Creating MainWindow");

                    var windowStopwatch = Stopwatch.StartNew();
                    var mainWindow = new MainWindow();
                    windowStopwatch.Stop();

                    LogToFile($"[{startupContextId}] MainWindow created in {windowStopwatch.ElapsedMilliseconds}ms");

                    windowStopwatch.Restart();
                    mainWindow.Show();
                    windowStopwatch.Stop();

                    LogToFile($"[{startupContextId}] MainWindow shown in {windowStopwatch.ElapsedMilliseconds}ms");

                    if (LogService != null)
                    {
                        LogService.Info($"Application startup completed in {_appLifetimeStopwatch.ElapsedMilliseconds}ms");
                    }
                }, "MainWindow creation", showErrorToUser: true);

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
            string updateContextId = $"UpdateCheck_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                if (LogService != null)
                {
                    LogService.LogDebug($"[{updateContextId}] Starting update check (silent={silent})");
                }

                var updateStopwatch = Stopwatch.StartNew();

                await UpdateService.CheckForUpdatesAsync(silent);

                updateStopwatch.Stop();

                if (LogService != null)
                {
                    LogService.LogDebug($"[{updateContextId}] Update check completed in {updateStopwatch.ElapsedMilliseconds}ms");

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
            string servicesContextId = $"Services_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                LogToFile($"[{servicesContextId}] Starting services initialization");

                var sw = Stopwatch.StartNew();

                LogToFile($"[{servicesContextId}] Initializing ConfigService");
                ConfigService = new ConfigurationService();
                LogToFile($"[{servicesContextId}] ConfigService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogToFile($"[{servicesContextId}] Loading settings from disk");
                await ConfigService.LoadSettingsAsync();
                LogToFile($"[{servicesContextId}] Settings loaded in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogToFile($"[{servicesContextId}] Initializing LogService");
                LogService = new LogService();
                LogService.Initialize(ConfigService);
                LogToFile($"[{servicesContextId}] LogService initialized in {sw.ElapsedMilliseconds}ms");

                LogService.Info($"[{servicesContextId}] Continuing service initialization with LogService");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing IconCacheService");
                IconCacheService = new IconCacheService();
                LogService.LogDebug($"[{servicesContextId}] IconCacheService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ManualGameIconService");
                ManualGameIconService = new ManualGameIconService();
                LogService.LogDebug($"[{servicesContextId}] ManualGameIconService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing GameDetectionService");
                GameDetectionService = new GameDetectionService();
                LogService.LogDebug($"[{servicesContextId}] GameDetectionService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ModDetectionService");
                ModDetectionService = new ModDetectionService();
                LogService.LogDebug($"[{servicesContextId}] ModDetectionService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing GameBackupService");
                GameBackupService = new GameBackupService();
                LogService.LogDebug($"[{servicesContextId}] GameBackupService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing ProfileService");
                ProfileService = new ProfileService(ConfigService);
                LogService.LogDebug($"[{servicesContextId}] ProfileService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Initializing UpdateService");
                UpdateService = new UpdateService(GITHUB_OWNER, GITHUB_REPO);
                LogService.LogDebug($"[{servicesContextId}] UpdateService initialized in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Loading profiles");
                await ProfileService.LoadProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profiles loaded in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Deduplicating profiles");
                await ProfileService.DeduplicateProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profile deduplication completed in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Migrating game profiles");
                await ProfileService.MigrateGameProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profile migration completed in {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                LogService.LogDebug($"[{servicesContextId}] Saving profiles");
                await ProfileService.SaveProfilesAsync();
                LogService.LogDebug($"[{servicesContextId}] Profiles saved in {sw.ElapsedMilliseconds}ms");

                LogService.Info($"[{servicesContextId}] All services initialized successfully");
            }
            catch (Exception ex)
            {
                LogCategorizedError("ERROR in InitializeServices", ex, ErrorCategory.Services, servicesContextId);

                MessageBox.Show($"Error initializing services: {ex.Message}\n\nThe application may not function correctly.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);

                InitializeFallbackServices();
            }
        }

        private void InitializeFallbackServices()
        {
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

        private void ApplyLowUsageMode()
        {
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

                GC.Collect(2, GCCollectionMode.Forced, true, true);

                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

                lowUsageSw.Stop();

                if (LogService != null)
                {
                    LogService.LogDebug($"Low Usage Mode applied in {lowUsageSw.ElapsedMilliseconds}ms");

                    LogService.LogDebug($"Working set after Low Usage Mode: {Environment.WorkingSet / 1024 / 1024} MB");
                }
            }
        }

        public static void LogToFile(string message, bool isDetailedLog = false)
        {
            try
            {
                if (LogService == null)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logMessage = $"[{timestamp}] {message}";
                    File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
                    return;
                }

                if (isDetailedLog)
                {
                    LogService.LogDebug(message);
                }
                else
                {
                    LogService.Info(message);
                }
            }
            catch
            {
            }
        }

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

                    if (ex.InnerException != null)
                    {
                        LogToFile($"Inner exception: {ex.InnerException.Message} - {ex.InnerException.StackTrace}", true);
                    }
                }

                return;
            }

            LogService.Error(fullMessage);

            if (ex != null)
            {
                LogService.LogDebug($"{contextStr}{categoryStr} Exception type: {ex.GetType().Name}");
                LogService.LogDebug($"{contextStr}{categoryStr} Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    LogService.LogDebug($"{contextStr}{categoryStr} Inner exception: {ex.InnerException.Message}");
                    LogService.LogDebug($"{contextStr}{categoryStr} Inner stack trace: {ex.InnerException.StackTrace}");
                }

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

        public static void LogError(string message, Exception ex = null)
        {
            LogCategorizedError(message, ex, ErrorCategory.Unknown);
        }

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

        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string exceptionContextId = $"UnhandledException_{DateTime.Now:yyyyMMdd_HHmmss}";

            ErrorCategory category = DetermineErrorCategory(e.Exception);

            LogCategorizedError("UNHANDLED EXCEPTION", e.Exception, category, exceptionContextId);

            MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nPlease restart the application.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            if (category == ErrorCategory.Startup || category == ErrorCategory.Services)
            {
                try
                {
                    CreateMiniDump(exceptionContextId);
                }
                catch
                {
                }
            }

            e.Handled = true;
        }

        private ErrorCategory DetermineErrorCategory(Exception ex)
        {
            if (ex == null) return ErrorCategory.Unknown;

            if (ex is IOException || ex is UnauthorizedAccessException || ex is FileNotFoundException)
                return ErrorCategory.FileSystem;

            if (ex is System.Net.WebException || ex is System.Net.Sockets.SocketException)
                return ErrorCategory.Network;

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

            if (_appLifetimeStopwatch.ElapsedMilliseconds < 10000)
                return ErrorCategory.Startup;

            return ErrorCategory.Unknown;
        }

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

            if (LogService != null)
            {
                LogService.LogDebug($"[{contextId}] Mini dump would be created at: {dumpPath}");
            }
            else
            {
                LogToFile($"[{contextId}] Mini dump would be created at: {dumpPath}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            string shutdownContextId = $"Shutdown_{DateTime.Now:yyyyMMdd_HHmmss}";

            try
            {
                base.OnExit(e);

                _appLifetimeStopwatch.Stop();

                if (LogService != null)
                {
                    LogService.Info($"[{shutdownContextId}] Application exiting with code: {e.ApplicationExitCode}");
                    LogService.Info($"[{shutdownContextId}] Total runtime: {_appLifetimeStopwatch.ElapsedMilliseconds}ms");

                    LogService.LogDebug($"[{shutdownContextId}] Performing final logging flush");
                }
                else
                {
                    LogToFile($"[{shutdownContextId}] Application exiting with code: {e.ApplicationExitCode}");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string message = $"[{timestamp}] [{shutdownContextId}] Error during shutdown: {ex.Message}";
                    File.AppendAllText(LogFilePath, message + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
using AMO_Launcher.Services;
using System;
using System.IO;
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

        // Log file for debugging
        private static readonly string LogFilePath;

        // GitHub repository info for updates
        private const string GITHUB_OWNER = "KolarF1";
        private const string GITHUB_REPO = "AMO-Launcher";

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
        }

        public App()
        {
            // Hook up the unhandled exception handler
            this.DispatcherUnhandledException += Application_DispatcherUnhandledException;

            // Start with a clean log
            if (File.Exists(LogFilePath))
            {
                try { File.Delete(LogFilePath); } catch { }
            }

            LogToFile("Application starting");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
                LogToFile("OnStartup called");

                // Initialize application services
                InitializeServices();
                LogToFile("Services initialized");

                // Create and show the main window
                LogToFile("Creating MainWindow");
                var mainWindow = new MainWindow();
                LogToFile("Showing MainWindow");
                mainWindow.Show();
                LogToFile("MainWindow shown");

                // Check for updates in the background after startup
                LogToFile("Checking for updates in the background");
                CheckForUpdatesAsync(true);
            }
            catch (Exception ex)
            {
                LogToFile($"FATAL ERROR in OnStartup: {ex}");
                MessageBox.Show($"Fatal error during startup: {ex.Message}\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckForUpdatesAsync(bool silent)
        {
            try
            {
                await UpdateService.CheckForUpdatesAsync(silent);
            }
            catch (Exception ex)
            {
                LogToFile($"Error checking for updates: {ex}");
            }
        }

        private async void InitializeServices()
        {
            try
            {
                LogToFile("Initializing ConfigService");
                // Initialize the configuration service first
                ConfigService = new ConfigurationService();

                LogToFile("Initializing IconCacheService");
                // Initialize the icon cache service
                IconCacheService = new IconCacheService();

                LogToFile("Initializing ManualGameIconService");
                // Initialize the manual game icon service
                ManualGameIconService = new ManualGameIconService();

                LogToFile("Initializing GameDetectionService");
                // Initialize the game detection service
                GameDetectionService = new GameDetectionService();

                LogToFile("Initializing ModDetectionService");
                // Initialize the mod detection service
                ModDetectionService = new ModDetectionService();

                LogToFile("Initializing GameBackupService");
                // Initialize the game backup service
                GameBackupService = new GameBackupService();

                LogToFile("Initializing ProfileService");
                // Initialize the profile service (after ConfigService)
                ProfileService = new ProfileService(ConfigService);

                LogToFile("Initializing UpdateService");
                // Initialize the update service
                UpdateService = new UpdateService(GITHUB_OWNER, GITHUB_REPO);

                // Load profiles from persistent storage
                await ProfileService.LoadProfilesAsync();
                LogToFile("Profiles loaded from persistent storage");

                // Run deduplication to clean up any existing duplicate profiles
                await ProfileService.DeduplicateProfilesAsync();

                // Normalize all game IDs to ensure consistency
                await ProfileService.MigrateGameProfilesAsync();

                // Save all changes to ensure profiles are consistent
                await ProfileService.SaveProfilesAsync();
                LogToFile("Profiles saved after normalization and deduplication");

                // Log successful initialization
                LogToFile("All services initialized successfully");
            }
            catch (Exception ex)
            {
                LogToFile($"ERROR in InitializeServices: {ex}");

                // Handle any initialization errors
                MessageBox.Show($"Error initializing services: {ex.Message}\n\nThe application may not function correctly.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Initialize with fallback empty services if needed
                if (ConfigService == null)
                {
                    LogToFile("Creating fallback ConfigService");
                    ConfigService = new ConfigurationService();
                }

                if (IconCacheService == null)
                {
                    LogToFile("Creating fallback IconCacheService");
                    IconCacheService = new IconCacheService();
                }

                if (ManualGameIconService == null)
                {
                    LogToFile("Creating fallback ManualGameIconService");
                    ManualGameIconService = new ManualGameIconService();
                }

                if (GameDetectionService == null)
                {
                    LogToFile("Creating fallback GameDetectionService");
                    GameDetectionService = new GameDetectionService();
                }

                if (ModDetectionService == null)
                {
                    LogToFile("Creating fallback ModDetectionService");
                    ModDetectionService = new ModDetectionService();
                }

                if (GameBackupService == null)
                {
                    LogToFile("Creating fallback GameBackupService");
                    GameBackupService = new GameBackupService();
                }

                if (ProfileService == null)
                {
                    LogToFile("Creating fallback ProfileService");
                    ProfileService = new ProfileService(ConfigService);
                }

                if (UpdateService == null)
                {
                    LogToFile("Creating fallback UpdateService");
                    UpdateService = new UpdateService(GITHUB_OWNER, GITHUB_REPO);
                }
            }
        }

        // Log to a file in AppData/Roaming/AMO_Launcher folder
        public static void LogToFile(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {message}";
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        // Handle unhandled exceptions to prevent crashes
        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogToFile($"UNHANDLED EXCEPTION: {e.Exception}");

            MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nPlease restart the application.\n\nSee log file in AppData/Roaming/AMO_Launcher for details.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Mark as handled to prevent application crash
            e.Handled = true;
        }
    }
}
using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AMO_Launcher.Services
{
    /// <summary>
    /// Service responsible for managing game backups, including version tracking and file copying
    /// </summary>
    public class GameBackupService
    {
        #region Constants

        // Name of the backup folder
        private const string BackupFolderName = "Original_GameData";

        // File to store version snapshots
        private const string VersionSnapshotsFileName = "version_snapshots.json";

        #endregion

        #region Fields

        // Path to the version snapshots file
        private string _versionSnapshotsFilePath;

        // Dictionary to store game version snapshots
        private Dictionary<string, string> _gameVersionSnapshots = new Dictionary<string, string>();

        #endregion

        #region Constructor

        public GameBackupService()
        {
            // Initialize using error handler with appropriate operation name
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.Info("Initializing GameBackupService");

                // Initialize version snapshots storage
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                // Ensure directory exists
                if (!Directory.Exists(appDataPath))
                {
                    App.LogService.LogDebug($"Creating application data directory: {appDataPath}");
                    Directory.CreateDirectory(appDataPath);
                }

                _versionSnapshotsFilePath = Path.Combine(appDataPath, VersionSnapshotsFileName);
                App.LogService.LogDebug($"Version snapshots file path: {_versionSnapshotsFilePath}");

                // Load existing version snapshots
                LoadVersionSnapshots();
            }, "GameBackupService initialization");
        }

        #endregion

        #region Version Snapshot Management

        /// <summary>
        /// Load version snapshots from storage
        /// </summary>
        private void LoadVersionSnapshots()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Loading version snapshots from: {_versionSnapshotsFilePath}");

                if (File.Exists(_versionSnapshotsFilePath))
                {
                    // Use performance tracking
                    using (var tracker = new PerformanceTracker("LoadVersionSnapshots"))
                    {
                        string json = File.ReadAllText(_versionSnapshotsFilePath);
                        _gameVersionSnapshots = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                             ?? new Dictionary<string, string>();

                        App.LogService.LogDebug($"Loaded {_gameVersionSnapshots.Count} version snapshots");

                        // Log detailed info in TRACE level
                        if (App.LogService.ShouldLogTrace())
                        {
                            foreach (var entry in _gameVersionSnapshots)
                            {
                                App.LogService.Trace($"Loaded version snapshot - Game: {entry.Key}, Version: {entry.Value}");
                            }
                        }
                    }
                }
                else
                {
                    App.LogService.LogDebug("Version snapshots file not found, creating new dictionary");
                    _gameVersionSnapshots = new Dictionary<string, string>();
                }
            }, "Loading version snapshots", false);
        }

        /// <summary>
        /// Save version snapshots to storage
        /// </summary>
        private void SaveVersionSnapshots()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Saving {_gameVersionSnapshots.Count} version snapshots");

                using (var tracker = new PerformanceTracker("SaveVersionSnapshots"))
                {
                    string json = JsonConvert.SerializeObject(_gameVersionSnapshots);
                    File.WriteAllText(_versionSnapshotsFilePath, json);

                    App.LogService.LogDebug("Version snapshots saved successfully");
                }
            }, "Saving version snapshots", false);
        }

        #endregion

        #region Game Version Management

        /// <summary>
        /// Check if the game version has changed
        /// </summary>
        /// <param name="game">Game information object</param>
        /// <returns>True if the version has changed, false otherwise</returns>
        public bool HasGameVersionChanged(GameInfo game)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Checking version changes for game: {game?.Name ?? "Unknown"}");

                // Validation
                if (game == null || string.IsNullOrEmpty(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
                {
                    App.LogService.Warning($"Unable to check version - Invalid game or missing executable");
                    return false;
                }

                // Skip check for F1 Manager games
                if (game.Name.Contains("Manager"))
                {
                    App.LogService.LogDebug($"Skipping version check for Manager game: {game.Name}");
                    return false;
                }

                // Generate a unique key for this game
                string gameKey = game.Id ?? game.ExecutablePath;

                // Get the current version from the executable
                string currentVersion = GetExecutableVersion(game.ExecutablePath);

                App.LogService.LogDebug($"Current version for {game.Name}: {currentVersion}");

                // If we don't have a stored version, store this one and return false
                if (!_gameVersionSnapshots.TryGetValue(gameKey, out string storedVersion))
                {
                    App.LogService.Info($"No previous version found for {game.Name}, storing current version");
                    _gameVersionSnapshots[gameKey] = currentVersion;
                    SaveVersionSnapshots();
                    return false;
                }

                // Check if the version changed
                bool versionChanged = storedVersion != currentVersion;

                // If the version changed, update the stored version
                if (versionChanged)
                {
                    App.LogService.Info($"Game version changed for {game.Name} - Old: {storedVersion}, New: {currentVersion}");
                    _gameVersionSnapshots[gameKey] = currentVersion;
                    SaveVersionSnapshots();
                }
                else
                {
                    App.LogService.LogDebug($"No version change detected for {game.Name}");
                }

                return versionChanged;
            }, $"Checking game version for {game?.Name ?? "Unknown"}", false, false);
        }

        /// <summary>
        /// Get version information from an executable file
        /// </summary>
        /// <param name="executablePath">Path to the executable file</param>
        /// <returns>Version string combining file version and timestamp</returns>
        private string GetExecutableVersion(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Getting executable version for: {executablePath}");

                using (var tracker = new PerformanceTracker("GetExecutableVersion"))
                {
                    try
                    {
                        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                        string version = versionInfo.FileVersion ?? "";

                        // Get file last write time as fallback/additional check
                        DateTime lastWriteTime = File.GetLastWriteTime(executablePath);
                        string timestamp = lastWriteTime.ToString("yyyyMMddHHmmss");

                        string versionString = $"{version}_{timestamp}";
                        App.LogService.LogDebug($"Version info: {version}, Timestamp: {timestamp}");

                        return versionString;
                    }
                    catch (Exception ex)
                    {
                        // Log primary approach failure
                        LogCategorizedError(
                            $"Error getting file version, using fallback approach",
                            ex,
                            ErrorCategory.FileSystem);

                        // Fallback to file info
                        FileInfo fileInfo = new FileInfo(executablePath);
                        string fallbackVersion = $"{fileInfo.Length}_{fileInfo.LastWriteTime:yyyyMMddHHmmss}";

                        App.LogService.LogDebug($"Using fallback version: {fallbackVersion}");
                        return fallbackVersion;
                    }
                }
            }, "Getting executable version", false, DateTime.Now.ToString("yyyyMMddHHmmss"));
        }

        #endregion

        #region Game Backup Management

        /// <summary>
        /// Create a backup of original game files
        /// </summary>
        /// <param name="game">Game information object</param>
        /// <returns>True if backup successful or not needed, false otherwise</returns>
        public async Task<bool> EnsureOriginalGameDataBackupAsync(GameInfo game)
        {
            // Create a logging context for this operation
            string operationId = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Start flow tracking for this operation
                FlowTracker.StartFlow(operationId);
                App.LogService.Info($"[{operationId}] Ensuring original game data backup for {game?.Name ?? "Unknown"}");

                try
                {
                    // Skip for F1 Manager games
                    if (game.Name.Contains("Manager"))
                    {
                        App.LogService.LogDebug($"[{operationId}] Skipping backup for Manager game: {game.Name}");
                        return true; // No backup needed for Manager games
                    }

                    string gameInstallDir = game.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

                    // Log directory info
                    App.LogService.LogDebug($"[{operationId}] Game install directory: {gameInstallDir}");
                    App.LogService.LogDebug($"[{operationId}] Backup directory: {backupDir}");

                    // Check if the backup exists
                    bool backupExists = Directory.Exists(backupDir);
                    App.LogService.LogDebug($"[{operationId}] Backup directory exists: {backupExists}");

                    // Step: Check version
                    FlowTracker.StepFlow(operationId, "VersionCheck");

                    // Check if game version has changed (using our snapshot detection)
                    bool isVersionChange = HasGameVersionChanged(game);
                    App.LogService.LogDebug($"[{operationId}] Game version has changed: {isVersionChange}");

                    // Determine if backup is needed
                    bool backupNeeded = !backupExists || isVersionChange;
                    App.LogService.Info($"[{operationId}] Backup needed: {backupNeeded}");

                    if (backupNeeded)
                    {
                        // Step: Extract game year
                        FlowTracker.StepFlow(operationId, "ExtractYear");

                        // Determine the game year from the name (e.g., "F1 2022" -> "2022")
                        string gameYear = ExtractGameYear(game.Name);
                        App.LogService.LogDebug($"[{operationId}] Extracted game year: {gameYear}");

                        // Step: User prompt
                        FlowTracker.StepFlow(operationId, "UserPrompt");

                        // Prompt user to verify files
                        bool shouldProceed = await PromptUserToVerifyFiles(game, isVersionChange);
                        App.LogService.LogDebug($"[{operationId}] User confirmed to proceed: {shouldProceed}");

                        if (shouldProceed)
                        {
                            // Step: Create backup
                            FlowTracker.StepFlow(operationId, "CreateBackup");

                            // Create or recreate backup
                            await CreateGameBackupAsync(game, gameYear, operationId);

                            // Update our version snapshot (in case it wasn't already updated)
                            string gameKey = game.Id ?? game.ExecutablePath;
                            _gameVersionSnapshots[gameKey] = GetExecutableVersion(game.ExecutablePath);
                            SaveVersionSnapshots();

                            App.LogService.Info($"[{operationId}] Backup completed successfully");
                            return true;
                        }

                        App.LogService.Info($"[{operationId}] Backup operation cancelled by user");
                        return false; // User cancelled
                    }

                    App.LogService.Info($"[{operationId}] No backup needed");
                    return true; // No backup needed
                }
                catch (Exception ex)
                {
                    LogCategorizedError(
                        $"[{operationId}] Error ensuring game backup",
                        ex,
                        ErrorCategory.FileSystem);
                    throw; // Let the ErrorHandler handle it
                }
                finally
                {
                    // End flow tracking
                    FlowTracker.EndFlow(operationId);
                }
            }, $"Ensuring game data backup for {game?.Name ?? "Unknown"}", true, false);
        }

        /// <summary>
        /// Extract the year from the game name
        /// </summary>
        /// <param name="gameName">Name of the game</param>
        /// <returns>Year string or empty string if not found</returns>
        private string ExtractGameYear(string gameName)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Extracting year from game name: {gameName}");

                // Try to find a 4-digit year in the name
                var yearMatch = Regex.Match(gameName, @"20\d{2}");
                if (yearMatch.Success)
                {
                    App.LogService.LogDebug($"Found 4-digit year: {yearMatch.Value}");
                    return yearMatch.Value;
                }

                // Try to find a 2-digit year (like F122 -> 22)
                var shortYearMatch = Regex.Match(gameName, @"F1\s?(\d{2})");
                if (shortYearMatch.Success && shortYearMatch.Groups.Count > 1)
                {
                    string year = "20" + shortYearMatch.Groups[1].Value;
                    App.LogService.LogDebug($"Found 2-digit year, expanded to: {year}");
                    return year;
                }

                // If all else fails, return empty
                App.LogService.Warning($"Could not extract year from game name: {gameName}");
                return string.Empty;
            }, "Extracting game year", false, string.Empty);
        }

        /// <summary>
        /// Prompt the user to verify game files
        /// </summary>
        /// <param name="game">Game information object</param>
        /// <param name="isVersionChange">Whether the game version has changed</param>
        /// <returns>True if user confirmed, false otherwise</returns>
        private Task<bool> PromptUserToVerifyFiles(GameInfo game, bool isVersionChange)
        {
            return ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.LogDebug($"Prompting user to verify files for {game?.Name ?? "Unknown"}, version change: {isVersionChange}");

                bool result = false;

                // We need to use Dispatcher to show UI from a background thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new Views.GameVerificationDialog(game, isVersionChange);
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.ShowDialog();
                    result = dialog.UserConfirmed;

                    App.LogService.LogDebug($"User verification result: {(result ? "Confirmed" : "Cancelled")}");
                });

                return result;
            }, "Prompting user to verify files", false, false);
        }

        /// <summary>
        /// Create the backup of game files
        /// </summary>
        /// <param name="game">Game information object</param>
        /// <param name="gameYear">Year of the game</param>
        /// <param name="operationId">Operation ID for logging context</param>
        /// <returns>Task representing the backup operation</returns>
        private async Task CreateGameBackupAsync(GameInfo game, string gameYear, string operationId = null)
        {
            // If no operation ID provided, create one
            operationId = operationId ?? $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                using (var performance = new PerformanceTracker("CreateGameBackup", LogLevel.INFO, 60000)) // 1 minute warning threshold
                {
                    App.LogService.Info($"[{operationId}] Creating game backup for {game.Name}");

                    string gameInstallDir = game.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

                    // Create or clear the backup directory
                    if (Directory.Exists(backupDir))
                    {
                        App.LogService.LogDebug($"[{operationId}] Deleting existing backup directory");
                        Directory.Delete(backupDir, true);
                    }

                    App.LogService.LogDebug($"[{operationId}] Creating backup directory: {backupDir}");
                    Directory.CreateDirectory(backupDir);

                    // List of folders to back up, replacing (year) with the actual year
                    List<string> foldersToBackup = new List<string>
                    {
                        $"{gameYear}_asset_groups",
                        "audio",
                        "character_package",
                        "character_package_shared",
                        Path.Combine("environment_package", "cinematics"),
                        Path.Combine("environment_package", "core_assets"),
                        $"f1_{gameYear}_vehicle_package",
                        $"f2_{gameYear}_vehicle_package",
                        "videos"
                    };

                    App.LogService.LogDebug($"[{operationId}] Folders to backup: {string.Join(", ", foldersToBackup)}");

                    // Create a progress window to show copying status
                    var progressWindow = new Views.BackupProgressWindow();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressWindow.Owner = Application.Current.MainWindow;
                        progressWindow.SetGame(game);
                        progressWindow.Show();
                    });

                    try
                    {
                        // Copy each folder to the backup
                        int folderIndex = 0;
                        int foldersFound = 0;

                        foreach (string folder in foldersToBackup)
                        {
                            string sourcePath = Path.Combine(gameInstallDir, folder);
                            string targetPath = Path.Combine(backupDir, folder);

                            // Skip if source doesn't exist
                            if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                            {
                                App.LogService.LogDebug($"[{operationId}] Skipping non-existent source: {sourcePath}");
                                folderIndex++;
                                continue;
                            }

                            foldersFound++;
                            App.LogService.Info($"[{operationId}] Backing up: {folder}");

                            // Update progress window
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(
                                    folderIndex / (double)foldersToBackup.Count,
                                    $"Copying {folder}...");
                            });

                            // Create a stopwatch for this folder
                            var folderStopwatch = Stopwatch.StartNew();

                            // Copy folder/file
                            await Task.Run(() => CopyDirectory(sourcePath, targetPath, operationId));

                            folderStopwatch.Stop();
                            App.LogService.LogDebug($"[{operationId}] Copying {folder} took {folderStopwatch.ElapsedMilliseconds}ms");

                            folderIndex++;
                        }

                        // Complete the progress
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            progressWindow.UpdateProgress(1.0, "Backup completed successfully!");
                            progressWindow.SetCompleted();
                        });

                        App.LogService.Info($"[{operationId}] Backup completed - {foldersFound} folders copied");
                    }
                    catch (Exception ex)
                    {
                        // Show error in progress window
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            progressWindow.ShowError(ex.Message);
                        });

                        LogCategorizedError(
                            $"[{operationId}] Error during backup creation",
                            ex,
                            ErrorCategory.FileSystem);

                        throw; // Re-throw to be handled by caller
                    }
                }
            }, $"Creating game backup for {game?.Name ?? "Unknown"}", true);
        }

        /// <summary>
        /// Recursively copy a directory
        /// </summary>
        /// <param name="sourceDir">Source directory path</param>
        /// <param name="targetDir">Target directory path</param>
        /// <param name="operationId">Operation ID for logging context</param>
        private void CopyDirectory(string sourceDir, string targetDir, string operationId = null)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                // Create the target directory if it doesn't exist
                if (!Directory.Exists(targetDir))
                {
                    App.LogService.Trace($"[{operationId}] Creating directory: {targetDir}");
                    Directory.CreateDirectory(targetDir);
                }

                // Copy files
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(targetDir, fileName);

                    App.LogService.Trace($"[{operationId}] Copying file: {fileName}");
                    File.Copy(file, destFile, true);
                }

                // Copy subdirectories
                foreach (string directory in Directory.GetDirectories(sourceDir))
                {
                    string dirName = Path.GetFileName(directory);
                    string destDir = Path.Combine(targetDir, dirName);

                    // Recursively copy subdirectories
                    CopyDirectory(directory, destDir, operationId);
                }
            }, $"Copying directory from {sourceDir} to {targetDir}", false);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Enhanced error logging with categorization
        /// </summary>
        private void LogCategorizedError(string message, Exception ex, ErrorCategory category)
        {
            // Category prefix for error message
            string categoryPrefix = $"[{category}] ";

            // Basic logging
            App.LogService.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                // Log exception details in debug mode
                App.LogService.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                // Special handling for different categories
                switch (category)
                {
                    case ErrorCategory.FileSystem:
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            // Additional file system error details
                            App.LogService.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                        }
                        break;
                }

                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    App.LogService.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Enumeration of error categories for better organization
    /// </summary>
    public enum ErrorCategory
    {
        FileSystem,     // File and directory access errors
        Network,        // Network and connectivity issues
        ModProcessing,  // Errors during mod processing
        GameExecution,  // Game launch and execution errors
        Configuration,  // Configuration and settings errors
        UI,             // User interface errors
        Unknown         // Uncategorized errors
    }

    /// <summary>
    /// Simple performance tracking utility
    /// </summary>
    public class PerformanceTracker : IDisposable
    {
        private string _operationName;
        private Stopwatch _stopwatch;
        private LogLevel _logLevel;
        private long _warningThresholdMs;

        public PerformanceTracker(string operationName, LogLevel logLevel = LogLevel.DEBUG, long warningThresholdMs = 1000)
        {
            _operationName = operationName;
            _logLevel = logLevel;
            _warningThresholdMs = warningThresholdMs;

            _stopwatch = Stopwatch.StartNew();

            // Log the start
            switch (_logLevel)
            {
                case LogLevel.ERROR:
                    App.LogService.Error($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.WARNING:
                    App.LogService.Warning($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.INFO:
                    App.LogService.Info($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.DEBUG:
                    App.LogService.LogDebug($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.TRACE:
                    App.LogService.Trace($"[PERF] Starting: {_operationName}");
                    break;
            }
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            long elapsedMs = _stopwatch.ElapsedMilliseconds;

            // Format elapsed time
            string formattedTime = FormatTimeSpan(TimeSpan.FromMilliseconds(elapsedMs));

            // Determine log level - elevate to WARNING if threshold exceeded
            LogLevel logLevel = _logLevel;
            if (elapsedMs > _warningThresholdMs)
            {
                logLevel = LogLevel.WARNING;
            }

            // Log the completion time
            switch (logLevel)
            {
                case LogLevel.ERROR:
                    App.LogService.Error($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.WARNING:
                    App.LogService.Warning($"[PERF] Completed: {_operationName} took {formattedTime} (exceeded threshold of {_warningThresholdMs}ms)");
                    break;
                case LogLevel.INFO:
                    App.LogService.Info($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.DEBUG:
                    App.LogService.LogDebug($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.TRACE:
                    App.LogService.Trace($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
            }
        }

        // Format a time span for readability
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{timeSpan.TotalDays:0.#} days";
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.TotalHours:0.#} hours";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.TotalMinutes:0.#} minutes";
            }
            else if (timeSpan.TotalSeconds >= 1)
            {
                return $"{timeSpan.TotalSeconds:0.##} seconds";
            }
            else
            {
                return $"{timeSpan.TotalMilliseconds:0} ms";
            }
        }
    }

    /// <summary>
    /// Utility for tracking application flow
    /// </summary>
    public static class FlowTracker
    {
        // Track the start of a logical application flow
        public static void StartFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] START");
        }

        // Track the end of a logical application flow
        public static void EndFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] END");
        }

        // Track a step within a flow
        public static void StepFlow(string flowName, string stepName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] STEP: {stepName}");
        }
    }

    #endregion
}
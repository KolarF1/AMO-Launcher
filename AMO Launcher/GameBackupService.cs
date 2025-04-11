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
    public class GameBackupService
    {
        #region Constants

        private const string BackupFolderName = "Original_GameData";

        private const string VersionSnapshotsFileName = "version_snapshots.json";

        #endregion

        #region Fields

        private string _versionSnapshotsFilePath;

        private Dictionary<string, string> _gameVersionSnapshots = new Dictionary<string, string>();

        #endregion

        #region Constructor

        public GameBackupService()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.Info("Initializing GameBackupService");

                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                if (!Directory.Exists(appDataPath))
                {
                    App.LogService.LogDebug($"Creating application data directory: {appDataPath}");
                    Directory.CreateDirectory(appDataPath);
                }

                _versionSnapshotsFilePath = Path.Combine(appDataPath, VersionSnapshotsFileName);
                App.LogService.LogDebug($"Version snapshots file path: {_versionSnapshotsFilePath}");

                LoadVersionSnapshots();
            }, "GameBackupService initialization");
        }

        #endregion

        #region Version Snapshot Management

        private void LoadVersionSnapshots()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Loading version snapshots from: {_versionSnapshotsFilePath}");

                if (File.Exists(_versionSnapshotsFilePath))
                {
                    using (var tracker = new PerformanceTracker("LoadVersionSnapshots"))
                    {
                        string json = File.ReadAllText(_versionSnapshotsFilePath);
                        _gameVersionSnapshots = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                             ?? new Dictionary<string, string>();

                        App.LogService.LogDebug($"Loaded {_gameVersionSnapshots.Count} version snapshots");

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

        public bool HasGameVersionChanged(GameInfo game)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Checking version changes for game: {game?.Name ?? "Unknown"}");

                if (game == null || string.IsNullOrEmpty(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
                {
                    App.LogService.Warning($"Unable to check version - Invalid game or missing executable");
                    return false;
                }

                if (game.Name.Contains("Manager"))
                {
                    App.LogService.LogDebug($"Skipping version check for Manager game: {game.Name}");
                    return false;
                }

                string gameKey = game.Id ?? game.ExecutablePath;

                string currentVersion = GetExecutableVersion(game.ExecutablePath);

                App.LogService.LogDebug($"Current version for {game.Name}: {currentVersion}");

                if (!_gameVersionSnapshots.TryGetValue(gameKey, out string storedVersion))
                {
                    App.LogService.Info($"No previous version found for {game.Name}, storing current version");
                    _gameVersionSnapshots[gameKey] = currentVersion;
                    SaveVersionSnapshots();
                    return false;
                }

                bool versionChanged = storedVersion != currentVersion;

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

                        DateTime lastWriteTime = File.GetLastWriteTime(executablePath);
                        string timestamp = lastWriteTime.ToString("yyyyMMddHHmmss");

                        string versionString = $"{version}_{timestamp}";
                        App.LogService.LogDebug($"Version info: {version}, Timestamp: {timestamp}");

                        return versionString;
                    }
                    catch (Exception ex)
                    {
                        LogCategorizedError(
                            $"Error getting file version, using fallback approach",
                            ex,
                            ErrorCategory.FileSystem);

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

        public async Task<bool> EnsureOriginalGameDataBackupAsync(GameInfo game)
        {
            string operationId = $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                FlowTracker.StartFlow(operationId);
                App.LogService.Info($"[{operationId}] Ensuring original game data backup for {game?.Name ?? "Unknown"}");

                try
                {
                    if (game.Name.Contains("Manager"))
                    {
                        App.LogService.LogDebug($"[{operationId}] Skipping backup for Manager game: {game.Name}");
                        return true;
                    }

                    string gameInstallDir = game.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

                    App.LogService.LogDebug($"[{operationId}] Game install directory: {gameInstallDir}");
                    App.LogService.LogDebug($"[{operationId}] Backup directory: {backupDir}");

                    bool backupExists = Directory.Exists(backupDir);
                    App.LogService.LogDebug($"[{operationId}] Backup directory exists: {backupExists}");

                    FlowTracker.StepFlow(operationId, "VersionCheck");

                    bool isVersionChange = HasGameVersionChanged(game);
                    App.LogService.LogDebug($"[{operationId}] Game version has changed: {isVersionChange}");

                    bool backupNeeded = !backupExists || isVersionChange;
                    App.LogService.Info($"[{operationId}] Backup needed: {backupNeeded}");

                    if (backupNeeded)
                    {
                        FlowTracker.StepFlow(operationId, "ExtractYear");

                        string gameYear = ExtractGameYear(game.Name);
                        App.LogService.LogDebug($"[{operationId}] Extracted game year: {gameYear}");

                        FlowTracker.StepFlow(operationId, "UserPrompt");

                        bool shouldProceed = await PromptUserToVerifyFiles(game, isVersionChange);
                        App.LogService.LogDebug($"[{operationId}] User confirmed to proceed: {shouldProceed}");

                        if (shouldProceed)
                        {
                            FlowTracker.StepFlow(operationId, "CreateBackup");

                            await CreateGameBackupAsync(game, gameYear, operationId);

                            string gameKey = game.Id ?? game.ExecutablePath;
                            _gameVersionSnapshots[gameKey] = GetExecutableVersion(game.ExecutablePath);
                            SaveVersionSnapshots();

                            App.LogService.Info($"[{operationId}] Backup completed successfully");
                            return true;
                        }

                        App.LogService.Info($"[{operationId}] Backup operation cancelled by user");
                        return false;
                    }

                    App.LogService.Info($"[{operationId}] No backup needed");
                    return true;
                }
                catch (Exception ex)
                {
                    LogCategorizedError(
                        $"[{operationId}] Error ensuring game backup",
                        ex,
                        ErrorCategory.FileSystem);
                    throw;
                }
                finally
                {
                    FlowTracker.EndFlow(operationId);
                }
            }, $"Ensuring game data backup for {game?.Name ?? "Unknown"}", true, false);
        }

        private string ExtractGameYear(string gameName)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Extracting year from game name: {gameName}");

                var yearMatch = Regex.Match(gameName, @"20\d{2}");
                if (yearMatch.Success)
                {
                    App.LogService.LogDebug($"Found 4-digit year: {yearMatch.Value}");
                    return yearMatch.Value;
                }

                var shortYearMatch = Regex.Match(gameName, @"F1\s?(\d{2})");
                if (shortYearMatch.Success && shortYearMatch.Groups.Count > 1)
                {
                    string year = "20" + shortYearMatch.Groups[1].Value;
                    App.LogService.LogDebug($"Found 2-digit year, expanded to: {year}");
                    return year;
                }

                App.LogService.Warning($"Could not extract year from game name: {gameName}");
                return string.Empty;
            }, "Extracting game year", false, string.Empty);
        }

        private Task<bool> PromptUserToVerifyFiles(GameInfo game, bool isVersionChange)
        {
            return ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.LogDebug($"Prompting user to verify files for {game?.Name ?? "Unknown"}, version change: {isVersionChange}");

                bool result = false;

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

        private async Task CreateGameBackupAsync(GameInfo game, string gameYear, string operationId = null)
        {
            operationId = operationId ?? $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                using (var performance = new PerformanceTracker("CreateGameBackup", LogLevel.INFO, 60000))
                {
                    App.LogService.Info($"[{operationId}] Creating game backup for {game.Name}");

                    string gameInstallDir = game.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

                    if (Directory.Exists(backupDir))
                    {
                        App.LogService.LogDebug($"[{operationId}] Deleting existing backup directory");
                        Directory.Delete(backupDir, true);
                    }

                    App.LogService.LogDebug($"[{operationId}] Creating backup directory: {backupDir}");
                    Directory.CreateDirectory(backupDir);

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

                    var progressWindow = new Views.BackupProgressWindow();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressWindow.Owner = Application.Current.MainWindow;
                        progressWindow.SetGame(game);
                        progressWindow.Show();
                    });

                    try
                    {
                        int folderIndex = 0;
                        int foldersFound = 0;

                        foreach (string folder in foldersToBackup)
                        {
                            string sourcePath = Path.Combine(gameInstallDir, folder);
                            string targetPath = Path.Combine(backupDir, folder);

                            if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                            {
                                App.LogService.LogDebug($"[{operationId}] Skipping non-existent source: {sourcePath}");
                                folderIndex++;
                                continue;
                            }

                            foldersFound++;
                            App.LogService.Info($"[{operationId}] Backing up: {folder}");

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(
                                    folderIndex / (double)foldersToBackup.Count,
                                    $"Copying {folder}...");
                            });

                            var folderStopwatch = Stopwatch.StartNew();

                            await Task.Run(() => CopyDirectory(sourcePath, targetPath, operationId));

                            folderStopwatch.Stop();
                            App.LogService.LogDebug($"[{operationId}] Copying {folder} took {folderStopwatch.ElapsedMilliseconds}ms");

                            folderIndex++;
                        }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            progressWindow.UpdateProgress(1.0, "Backup completed successfully!");
                            progressWindow.SetCompleted();
                        });

                        App.LogService.Info($"[{operationId}] Backup completed - {foldersFound} folders copied");
                    }
                    catch (Exception ex)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            progressWindow.ShowError(ex.Message);
                        });

                        LogCategorizedError(
                            $"[{operationId}] Error during backup creation",
                            ex,
                            ErrorCategory.FileSystem);

                        throw;
                    }
                }
            }, $"Creating game backup for {game?.Name ?? "Unknown"}", true);
        }

        private void CopyDirectory(string sourceDir, string targetDir, string operationId = null)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (!Directory.Exists(targetDir))
                {
                    App.LogService.Trace($"[{operationId}] Creating directory: {targetDir}");
                    Directory.CreateDirectory(targetDir);
                }

                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(targetDir, fileName);

                    App.LogService.Trace($"[{operationId}] Copying file: {fileName}");
                    File.Copy(file, destFile, true);
                }

                foreach (string directory in Directory.GetDirectories(sourceDir))
                {
                    string dirName = Path.GetFileName(directory);
                    string destDir = Path.Combine(targetDir, dirName);

                    CopyDirectory(directory, destDir, operationId);
                }
            }, $"Copying directory from {sourceDir} to {targetDir}", false);
        }

        #endregion

        #region Helper Methods

        private void LogCategorizedError(string message, Exception ex, ErrorCategory category)
        {
            string categoryPrefix = $"[{category}] ";

            App.LogService.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                App.LogService.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                switch (category)
                {
                    case ErrorCategory.FileSystem:
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            App.LogService.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                        }
                        break;
                }

                if (ex.InnerException != null)
                {
                    App.LogService.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        #endregion
    }

    #region Helper Classes

    public enum ErrorCategory
    {
        FileSystem,
        Network,
        ModProcessing,
        GameExecution,
        Configuration,
        UI,
        Unknown
    }

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

            string formattedTime = FormatTimeSpan(TimeSpan.FromMilliseconds(elapsedMs));

            LogLevel logLevel = _logLevel;
            if (elapsedMs > _warningThresholdMs)
            {
                logLevel = LogLevel.WARNING;
            }

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

    public static class FlowTracker
    {
        public static void StartFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] START");
        }

        public static void EndFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] END");
        }

        public static void StepFlow(string flowName, string stepName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] STEP: {stepName}");
        }
    }

    #endregion
}
using AMO_Launcher.Models;
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
        // Name of the backup folder
        private const string BackupFolderName = "Original_GameData";

        // File to store version snapshots
        private const string VersionSnapshotsFileName = "version_snapshots.json";

        // Path to the version snapshots file
        private readonly string _versionSnapshotsFilePath;

        // Dictionary to store game version snapshots
        private Dictionary<string, string> _gameVersionSnapshots = new Dictionary<string, string>();

        public GameBackupService()
        {
            // Initialize version snapshots storage
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            // Ensure directory exists
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _versionSnapshotsFilePath = Path.Combine(appDataPath, VersionSnapshotsFileName);

            // Load existing version snapshots
            LoadVersionSnapshots();
        }

        // Load version snapshots from storage
        private void LoadVersionSnapshots()
        {
            try
            {
                if (File.Exists(_versionSnapshotsFilePath))
                {
                    string json = File.ReadAllText(_versionSnapshotsFilePath);
                    _gameVersionSnapshots = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                         ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading version snapshots: {ex.Message}");
                _gameVersionSnapshots = new Dictionary<string, string>();
            }
        }

        // Save version snapshots to storage
        private void SaveVersionSnapshots()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_gameVersionSnapshots);
                File.WriteAllText(_versionSnapshotsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving version snapshots: {ex.Message}");
            }
        }

        // Check if the game version has changed
        public bool HasGameVersionChanged(GameInfo game)
        {
            if (game == null || string.IsNullOrEmpty(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
            {
                return false;
            }

            // Skip check for F1 Manager games
            if (game.Name.Contains("Manager"))
            {
                return false;
            }

            // Generate a unique key for this game
            string gameKey = game.Id ?? game.ExecutablePath;

            // Get the current version from the executable
            string currentVersion = GetExecutableVersion(game.ExecutablePath);

            // If we don't have a stored version, store this one and return false
            if (!_gameVersionSnapshots.TryGetValue(gameKey, out string storedVersion))
            {
                _gameVersionSnapshots[gameKey] = currentVersion;
                SaveVersionSnapshots();
                return false;
            }

            // Check if the version changed
            bool versionChanged = storedVersion != currentVersion;

            // If the version changed, update the stored version
            if (versionChanged)
            {
                _gameVersionSnapshots[gameKey] = currentVersion;
                SaveVersionSnapshots();
            }

            return versionChanged;
        }

        // Get version information from an executable file
        private string GetExecutableVersion(string executablePath)
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                string version = versionInfo.FileVersion ?? "";

                // Get file last write time as fallback/additional check
                DateTime lastWriteTime = File.GetLastWriteTime(executablePath);
                string timestamp = lastWriteTime.ToString("yyyyMMddHHmmss");

                // Combine version and timestamp for a more robust check
                return $"{version}_{timestamp}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting file version: {ex.Message}");
                // Return file size and last write time as a fallback
                try
                {
                    FileInfo fileInfo = new FileInfo(executablePath);
                    return $"{fileInfo.Length}_{fileInfo.LastWriteTime:yyyyMMddHHmmss}";
                }
                catch
                {
                    // If all else fails, just return a timestamp
                    return DateTime.Now.ToString("yyyyMMddHHmmss");
                }
            }
        }

        // Create a backup of original game files
        public async Task<bool> EnsureOriginalGameDataBackupAsync(GameInfo game)
        {
            try
            {
                // Skip for F1 Manager games
                if (game.Name.Contains("Manager"))
                {
                    return true; // No backup needed for Manager games
                }

                string gameInstallDir = game.InstallDirectory;
                string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

                // Check if the backup exists
                bool backupExists = Directory.Exists(backupDir);

                // Check if game version has changed (using our snapshot detection)
                bool isVersionChange = HasGameVersionChanged(game);

                // Determine if backup is needed
                bool backupNeeded = !backupExists || isVersionChange;

                if (backupNeeded)
                {
                    // Determine the game year from the name (e.g., "F1 2022" -> "2022")
                    string gameYear = ExtractGameYear(game.Name);

                    // Prompt user to verify files
                    bool shouldProceed = await PromptUserToVerifyFiles(game, isVersionChange);

                    if (shouldProceed)
                    {
                        // Create or recreate backup
                        await CreateGameBackupAsync(game, gameYear);

                        // Update our version snapshot (in case it wasn't already updated)
                        string gameKey = game.Id ?? game.ExecutablePath;
                        _gameVersionSnapshots[gameKey] = GetExecutableVersion(game.ExecutablePath);
                        SaveVersionSnapshots();

                        return true;
                    }

                    return false; // User cancelled
                }

                return true; // No backup needed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ensuring game backup: {ex.Message}");
                MessageBox.Show(
                    $"Error while creating game backup: {ex.Message}\n\nYou may need to restart the launcher or verify your game files.",
                    "Backup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        // Extract the year from the game name
        private string ExtractGameYear(string gameName)
        {
            // Try to find a 4-digit year in the name
            var yearMatch = Regex.Match(gameName, @"20\d{2}");
            if (yearMatch.Success)
            {
                return yearMatch.Value;
            }

            // Try to find a 2-digit year (like F122 -> 22)
            var shortYearMatch = Regex.Match(gameName, @"F1\s?(\d{2})");
            if (shortYearMatch.Success && shortYearMatch.Groups.Count > 1)
            {
                return "20" + shortYearMatch.Groups[1].Value;
            }

            // If all else fails, return empty
            return string.Empty;
        }

        // Prompt the user to verify game files
        private Task<bool> PromptUserToVerifyFiles(GameInfo game, bool isVersionChange)
        {
            return Task.Run(() =>
            {
                bool result = false;

                // We need to use Dispatcher to show UI from a background thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Views.GameVerificationDialog(game, isVersionChange);
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.ShowDialog();
                    result = dialog.UserConfirmed;
                });

                return result;
            });
        }

        // Create the backup of game files
        private async Task CreateGameBackupAsync(GameInfo game, string gameYear)
        {
            string gameInstallDir = game.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, BackupFolderName);

            // Create or clear the backup directory
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }

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

            // Create a progress window to show copying status
            var progressWindow = new Views.BackupProgressWindow();
            progressWindow.Owner = Application.Current.MainWindow;
            progressWindow.SetGame(game);
            progressWindow.Show();

            try
            {
                // Copy each folder to the backup
                int folderIndex = 0;
                foreach (string folder in foldersToBackup)
                {
                    string sourcePath = Path.Combine(gameInstallDir, folder);
                    string targetPath = Path.Combine(backupDir, folder);

                    // Skip if source doesn't exist
                    if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                    {
                        folderIndex++;
                        continue;
                    }

                    // Update progress window
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow.UpdateProgress(
                            folderIndex / (double)foldersToBackup.Count,
                            $"Copying {folder}...");
                    });

                    // Copy folder/file
                    await Task.Run(() => CopyDirectory(sourcePath, targetPath));
                    folderIndex++;
                }

                // Complete the progress
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow.UpdateProgress(1.0, "Backup completed successfully!");
                    progressWindow.SetCompleted();
                });
            }
            catch (Exception ex)
            {
                // Show error in progress window
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow.ShowError(ex.Message);
                });
                throw; // Re-throw to be handled by caller
            }
        }

        // Recursively copy a directory
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            // Create the target directory if it doesn't exist
            Directory.CreateDirectory(targetDir);

            // Copy files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Copy subdirectories
            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(directory);
                string destDir = Path.Combine(targetDir, dirName);
                CopyDirectory(directory, destDir);
            }
        }
    }
}
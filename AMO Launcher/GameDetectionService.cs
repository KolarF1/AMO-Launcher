using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AMO_Launcher.Models;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Services
{
    public class GameDetectionService
    {
        private readonly ConfigurationService _configService;
        private readonly IconCacheService _iconCacheService;

        private readonly List<string> _commonPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games")
        };

        private readonly List<(string pattern, string gameName)> _knownF1Games = new List<(string, string)>
        {
            ("F1_20*.exe", null),
            ("F1_22.exe", "F1 22"),
            ("F1_23.exe", "F1 23"),
            ("F1_24.exe", "F1 24"),
            ("F1_25.exe", "F1 25"),
            ("F1Manager*.exe", null),
            ("F1Manager22.exe", "F1 Manager 22"),
            ("F1Manager23.exe", "F1 Manager 23"),
        };

        private Dictionary<string, List<string>> _foundGameExes = new Dictionary<string, List<string>>();

        public GameDetectionService()
        {
            _configService = App.ConfigService;
            _iconCacheService = App.IconCacheService;
            App.LogService?.LogDebug("GameDetectionService initialized");
        }

        public async Task<List<GameInfo>> ScanForGamesAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Starting game detection scan");
                var startTime = DateTime.Now;
                var results = new List<GameInfo>();
                var removedGamePaths = _configService.GetRemovedGamePaths();

                _foundGameExes.Clear();

                await Task.Run(() => TryScanSteamRegistry(results, removedGamePaths));

                await Task.Run(() => TryScanRegistryInstalls(results, removedGamePaths));

                foreach (var path in _commonPaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(path))
                        {
                            App.LogService?.LogDebug($"Scanning common path: {path}");
                            ScanDirectoryForGames(path, results, removedGamePaths);
                        }
                        else
                        {
                            App.LogService?.Trace($"Skipping non-existent path: {path}");
                        }
                    });
                }

                var customPaths = _configService.GetCustomGameScanPaths();
                foreach (var path in customPaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(path))
                        {
                            App.LogService?.LogDebug($"Scanning custom path: {path}");
                            ScanDirectoryForGames(path, results, removedGamePaths);
                        }
                        else
                        {
                            App.LogService?.Warning($"Custom path not found: {path}");
                        }
                    });
                }

                var uniqueResults = results.GroupBy(g => g.ExecutablePath)
                                         .Select(g => g.First())
                                         .ToList();

                var elapsed = DateTime.Now - startTime;
                App.LogService?.Info($"Game scan complete. Found {uniqueResults.Count} games in {elapsed.TotalSeconds:F2} seconds");

                if (App.LogService?.ShouldLogDebug() == true)
                {
                    foreach (var game in uniqueResults)
                    {
                        App.LogService?.LogDebug($"Detected game: {game.Name} at {game.ExecutablePath}");
                    }
                }

                return uniqueResults;
            }, "ScanForGames", true, new List<GameInfo>());
        }

        private void ScanDirectoryForGames(string directory, List<GameInfo> results, List<string> removedGamePaths)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"Scanning directory: {directory}");

                _foundGameExes.Clear();

                CollectGameExecutables(directory, 0, 3);

                foreach (var gameName in _foundGameExes.Keys)
                {
                    var exePaths = _foundGameExes[gameName];
                    App.LogService?.Trace($"Found {exePaths.Count} executables for {gameName}");

                    if (exePaths.Count == 1)
                    {
                        string exePath = exePaths[0];

                        if (removedGamePaths.Contains(exePath))
                        {
                            App.LogService?.LogDebug($"Skipping previously removed game: {exePath}");
                            continue;
                        }

                        AddGameToResults(exePath, results);
                    }
                    else if (exePaths.Count > 1)
                    {
                        string preferredExe = ChoosePreferredExecutable(gameName, exePaths);

                        if (removedGamePaths.Contains(preferredExe))
                        {
                            App.LogService?.LogDebug($"Skipping previously removed game: {preferredExe}");
                            continue;
                        }

                        AddGameToResults(preferredExe, results);
                    }
                }

                App.LogService?.Trace($"Directory scan complete for: {directory}");
            }, $"Scanning directory {directory}", false);
        }

        private void CollectGameExecutables(string directory, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                foreach (var file in Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        bool isF1Game = false;
                        string gameName = null;

                        foreach (var (pattern, specificGameName) in _knownF1Games)
                        {
                            if (MatchesPattern(fileName, pattern))
                            {
                                isF1Game = true;
                                gameName = specificGameName ?? DeriveGameName(fileName.Replace("_dx12", ""));
                                break;
                            }
                        }

                        if (isF1Game)
                        {
                            if (!_foundGameExes.ContainsKey(gameName))
                            {
                                _foundGameExes[gameName] = new List<string>();
                            }

                            _foundGameExes[gameName].Add(file);
                            App.LogService?.Trace($"Found potential game exe: {gameName} - {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogService?.LogDebug($"Error checking file {file}: {ex.Message}");
                    }
                }

                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    try
                    {
                        CollectGameExecutables(subDir, currentDepth + 1, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        App.LogService?.LogDebug($"Error accessing directory {subDir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogService?.LogDebug($"Error accessing directory {directory}: {ex.Message}");
            }
        }

        private string ChoosePreferredExecutable(string gameName, List<string> exePaths)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Choosing preferred executable for {gameName} from {exePaths.Count} options");

                if (gameName.Contains("Manager"))
                {
                    foreach (var exePath in exePaths)
                    {
                        string directory = Path.GetDirectoryName(exePath);
                        bool isInWin64 = directory != null &&
                            (directory.Contains("\\Binaries\\Win64") ||
                             directory.Contains("/Binaries/Win64"));

                        if (!isInWin64)
                        {
                            App.LogService?.LogDebug($"Selected base directory exe for F1 Manager: {exePath}");
                            return exePath;
                        }
                    }
                }
                else
                {
                    string nonDx12Exe = null;
                    string dx12Exe = null;

                    foreach (var exePath in exePaths)
                    {
                        string fileName = Path.GetFileName(exePath);
                        if (fileName.Contains("_dx12"))
                        {
                            dx12Exe = exePath;
                            App.LogService?.Trace($"Found DX12 exe: {exePath}");
                        }
                        else
                        {
                            nonDx12Exe = exePath;
                            App.LogService?.Trace($"Found non-DX12 exe: {exePath}");
                        }
                    }

                    if (nonDx12Exe != null)
                    {
                        App.LogService?.LogDebug($"Selected non-DX12 exe for F1 game: {nonDx12Exe}");
                        return nonDx12Exe;
                    }

                    if (dx12Exe != null)
                    {
                        App.LogService?.LogDebug($"Selected DX12 exe for F1 game (no alternative): {dx12Exe}");
                        return dx12Exe;
                    }
                }

                App.LogService?.LogDebug($"No preferred exe found for {gameName}, using first option: {exePaths[0]}");
                return exePaths[0];
            }, $"Choosing preferred executable for {gameName}", false, exePaths.FirstOrDefault());
        }

        private void AddGameToResults(string exePath, List<GameInfo> results)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                string fileName = Path.GetFileName(exePath);
                string gameName = null;

                foreach (var (pattern, specificGameName) in _knownF1Games)
                {
                    if (MatchesPattern(fileName, pattern))
                    {
                        gameName = specificGameName ?? DeriveGameName(fileName.Replace("_dx12", ""));
                        break;
                    }
                }

                if (gameName == null)
                {
                    App.LogService?.LogDebug($"Skipping unknown game executable: {exePath}");
                    return;
                }

                var gameInfo = new GameInfo
                {
                    ExecutablePath = exePath,
                    InstallDirectory = Path.GetDirectoryName(exePath),
                    Name = gameName,
                    Version = GetFileVersion(exePath),
                    Icon = _iconCacheService.GetIcon(exePath)
                };
                gameInfo.GenerateId();

                if (!results.Any(g => g.ExecutablePath == gameInfo.ExecutablePath))
                {
                    results.Add(gameInfo);
                    App.LogService?.Info($"Added game: {gameInfo.Name} ({gameInfo.ExecutablePath})");
                }
                else
                {
                    App.LogService?.LogDebug($"Skipped duplicate game: {gameInfo.Name} ({gameInfo.ExecutablePath})");
                }
            }, $"Adding game {exePath} to results", false);
        }

        private void ScanDirectoryRecursive(string directory, List<GameInfo> results, int currentDepth, int maxDepth, List<string> removedGamePaths)
        {
            if (currentDepth > maxDepth) return;

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"Recursively scanning directory: {directory} (Depth: {currentDepth}/{maxDepth})");

                foreach (var file in Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    if (removedGamePaths.Contains(file))
                    {
                        App.LogService?.Trace($"Skipping previously removed game: {file}");
                        continue;
                    }

                    CheckIfF1Game(file, results);
                }

                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    ScanDirectoryRecursive(subDir, results, currentDepth + 1, maxDepth, removedGamePaths);
                }
            }, $"Scanning directory {directory} recursively", false);
        }

        private void CheckIfF1Game(string exePath, List<GameInfo> results)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                string fileName = Path.GetFileName(exePath);
                App.LogService?.Trace($"Checking if {fileName} is an F1 game");

                foreach (var (pattern, gameName) in _knownF1Games)
                {
                    if (MatchesPattern(fileName, pattern))
                    {
                        var gameInfo = new GameInfo
                        {
                            ExecutablePath = exePath,
                            InstallDirectory = Path.GetDirectoryName(exePath),
                            Name = gameName ?? DeriveGameName(fileName),
                            Version = GetFileVersion(exePath),
                            Icon = _iconCacheService.GetIcon(exePath)
                        };
                        gameInfo.GenerateId();

                        if (!results.Any(g => g.ExecutablePath == gameInfo.ExecutablePath))
                        {
                            results.Add(gameInfo);
                            App.LogService?.LogDebug($"Added F1 game: {gameInfo.Name} ({gameInfo.ExecutablePath})");
                        }
                        else
                        {
                            App.LogService?.Trace($"Skipped duplicate F1 game: {gameInfo.Name}");
                        }

                        break;
                    }
                }
            }, $"Checking if {Path.GetFileName(exePath)} is an F1 game", false);
        }

        private void TryScanSteamRegistry(List<GameInfo> results, List<string> removedGamePaths)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Scanning Steam registry for games");
                var startTime = DateTime.Now;

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            steamPath = steamPath.Replace("/", "\\");
                            App.LogService?.LogDebug($"Found Steam path: {steamPath}");

                            string commonPath = Path.Combine(steamPath, "steamapps", "common");
                            if (Directory.Exists(commonPath))
                            {
                                App.LogService?.LogDebug($"Scanning main Steam library: {commonPath}");
                                ScanDirectoryForGames(commonPath, results, removedGamePaths);
                            }
                            else
                            {
                                App.LogService?.LogDebug($"Main Steam library path not found: {commonPath}");
                            }

                            string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                            if (File.Exists(libraryFoldersPath))
                            {
                                App.LogService?.LogDebug($"Found Steam library folders file: {libraryFoldersPath}");

                                int librariesFound = 0;
                                foreach (var line in File.ReadAllLines(libraryFoldersPath))
                                {
                                    if (line.Contains("\"path\""))
                                    {
                                        int startIndex = line.IndexOf('"', line.IndexOf('"') + 1) + 1;
                                        int endIndex = line.LastIndexOf('"');
                                        if (startIndex > 0 && endIndex > startIndex)
                                        {
                                            string libraryPath = line.Substring(startIndex, endIndex - startIndex)
                                                .Replace("\\\\", "\\");

                                            string libCommonPath = Path.Combine(libraryPath, "steamapps", "common");
                                            if (Directory.Exists(libCommonPath))
                                            {
                                                librariesFound++;
                                                App.LogService?.LogDebug($"Scanning additional Steam library: {libCommonPath}");
                                                ScanDirectoryForGames(libCommonPath, results, removedGamePaths);
                                            }
                                            else
                                            {
                                                App.LogService?.LogDebug($"Additional Steam library path not found: {libCommonPath}");
                                            }
                                        }
                                    }
                                }
                                App.LogService?.LogDebug($"Processed {librariesFound} additional Steam libraries");
                            }
                            else
                            {
                                App.LogService?.LogDebug("No Steam library folders file found");
                            }
                        }
                        else
                        {
                            App.LogService?.LogDebug("No Steam path found in registry");
                        }
                    }
                    else
                    {
                        App.LogService?.LogDebug("Could not access Steam registry key");
                    }
                }

                var elapsed = DateTime.Now - startTime;
                App.LogService?.LogDebug($"Steam registry scan completed in {elapsed.TotalMilliseconds:F0}ms");
            }, "Scanning Steam registry", false);
        }

        private void TryScanRegistryInstalls(List<GameInfo> results, List<string> removedGamePaths)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Scanning Windows registry for game installations");
                var startTime = DateTime.Now;
                int gamesFound = 0;

                var registryKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var baseKeyPath in registryKeys)
                {
                    try
                    {
                        using (var baseKey = Registry.LocalMachine.OpenSubKey(baseKeyPath))
                        {
                            if (baseKey == null)
                            {
                                App.LogService?.LogDebug($"Registry key not found: {baseKeyPath}");
                                continue;
                            }

                            App.LogService?.LogDebug($"Scanning registry key: {baseKeyPath}");
                            int entriesChecked = 0;

                            foreach (var subKeyName in baseKey.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var subKey = baseKey.OpenSubKey(subKeyName))
                                    {
                                        if (subKey == null) continue;
                                        entriesChecked++;

                                        string displayName = subKey.GetValue("DisplayName") as string;
                                        if (!string.IsNullOrEmpty(displayName) &&
                                            (displayName.Contains("F1") || displayName.Contains("Formula 1")))
                                        {
                                            string installLocation = subKey.GetValue("InstallLocation") as string;
                                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                            {
                                                App.LogService?.LogDebug($"Found potential F1 game installation: {displayName} at {installLocation}");
                                                gamesFound++;
                                                ScanDirectoryForGames(installLocation, results, removedGamePaths);
                                            }
                                            else
                                            {
                                                App.LogService?.LogDebug($"Found F1 game in registry but location invalid: {displayName}, {installLocation}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    App.LogService?.LogDebug($"Error accessing registry subkey {subKeyName}: {ex.Message}");
                                }
                            }

                            App.LogService?.LogDebug($"Checked {entriesChecked} registry entries in {baseKeyPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogService?.Warning($"Could not access registry key {baseKeyPath}: {ex.Message}");
                    }
                }

                var elapsed = DateTime.Now - startTime;
                App.LogService?.Info($"Windows registry scan complete in {elapsed.TotalMilliseconds:F0}ms. Found {gamesFound} potential F1 game installations.");
            }, "Scanning Windows registry for games", false);
        }

        private bool MatchesPattern(string filename, string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(filename, regexPattern, RegexOptions.IgnoreCase);
        }

        private string DeriveGameName(string fileName)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"Deriving game name from: {fileName}");

                string name = Path.GetFileNameWithoutExtension(fileName);

                name = name.Replace('_', ' ');

                if (name.StartsWith("F1 20") || name.Contains("F1 20"))
                {
                    int startIndex = name.IndexOf("20");
                    if (startIndex >= 0 && startIndex + 4 <= name.Length)
                    {
                        string result = $"F1 {name.Substring(startIndex, 4)}";
                        App.LogService?.Trace($"Derived name (20XX format): {result}");
                        return result;
                    }
                }

                if (name.StartsWith("F1") && name.Length >= 4 && char.IsDigit(name[2]) && char.IsDigit(name[3]))
                {
                    string yearDigits = name.Substring(2, 2);
                    int year = int.TryParse(yearDigits, out int y) ? y : 0;

                    string result;
                    if (year >= 22)
                    {
                        result = $"F1 {yearDigits}";
                    }
                    else
                    {
                        result = $"F1 20{yearDigits}";
                    }

                    App.LogService?.Trace($"Derived name (F1XX format): {result}");
                    return result;
                }

                if (name.Contains("Manager"))
                {
                    var match = Regex.Match(name, @"(\d{2,4})");
                    if (match.Success)
                    {
                        string year = match.Groups[1].Value;
                        string result = $"F1 Manager {year}";
                        App.LogService?.Trace($"Derived name (Manager format with year): {result}");
                        return result;
                    }

                    App.LogService?.Trace($"Derived name (Manager format): F1 Manager");
                    return "F1 Manager";
                }

                string[] parts = name.Split(' ');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!string.IsNullOrEmpty(parts[i]))
                    {
                        parts[i] = char.ToUpper(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1) : "");
                    }
                }

                string finalResult = string.Join(" ", parts);
                App.LogService?.Trace($"Derived name (generic format): {finalResult}");
                return finalResult;
            }, $"Deriving game name from {fileName}", false, fileName);
        }

        private string GetFileVersion(string filePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                {
                    App.LogService?.Trace($"Got file version for {Path.GetFileName(filePath)}: {versionInfo.FileVersion}");
                    return versionInfo.FileVersion;
                }

                App.LogService?.Trace($"No file version found for {Path.GetFileName(filePath)}");
                return "Unknown";
            }, $"Getting file version for {Path.GetFileName(filePath)}", false, "Unknown");
        }

        public GameInfo AddGameManually(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService?.Warning($"Invalid executable path for manual game addition: {executablePath}");
                    return null;
                }

                App.LogService?.Info($"Manually adding game: {executablePath}");
                string fileName = Path.GetFileName(executablePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(executablePath);

                string displayName = fileNameWithoutExt.Replace('_', ' ');

                string derivedName = null;
                foreach (var (pattern, gameName) in _knownF1Games)
                {
                    if (MatchesPattern(fileName, pattern) && !string.IsNullOrEmpty(gameName))
                    {
                        derivedName = gameName;
                        App.LogService?.LogDebug($"Matched known game pattern: {pattern}, name: {gameName}");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(derivedName))
                {
                    derivedName = DeriveGameName(fileName);
                    App.LogService?.LogDebug($"No pattern match, derived game name: {derivedName}");
                }

                BitmapImage icon = _iconCacheService.GetIcon(executablePath);

                var gameInfo = new GameInfo
                {
                    ExecutablePath = executablePath,
                    InstallDirectory = Path.GetDirectoryName(executablePath),
                    Name = derivedName,
                    Version = GetFileVersion(executablePath),
                    Icon = icon,
                    IsManuallyAdded = true
                };
                gameInfo.GenerateId();

                _configService.RemoveFromRemovedGames(executablePath);

                App.LogService?.Info($"Successfully added game manually: {gameInfo.Name} ({gameInfo.ExecutablePath})");
                return gameInfo;
            }, "Adding game manually", true);
        }

        public bool GameExistsOnDisk(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool exists = !string.IsNullOrEmpty(executablePath) && File.Exists(executablePath);

                if (exists)
                {
                    App.LogService?.Trace($"Game exists on disk: {executablePath}");
                }
                else
                {
                    App.LogService?.LogDebug($"Game does not exist on disk: {executablePath}");
                }

                return exists;
            }, $"Checking if game exists: {executablePath}", false, false);
        }
    }
}
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

namespace AMO_Launcher.Services
{
    public class GameDetectionService
    {
        private readonly ConfigurationService _configService;
        private readonly IconCacheService _iconCacheService;

        // Common installation paths to check
        private readonly List<string> _commonPaths = new List<string>
        {
            // Steam default paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"),
            // Epic Games default path
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
            // Origin/EA App default paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
            // Additional common paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Origin Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games")
        };

        // Known F1 game executable patterns
        private readonly List<(string pattern, string gameName)> _knownF1Games = new List<(string, string)>
        {
            ("F1_20*.exe", null),        // F1 games (2020, 2021, etc.)
            ("F1_22.exe", "F1 22"),     // F1 22
            ("F1_23.exe", "F1 23"),     // F1 23
            ("F1_24.exe", "F1 24"),     // F1 24
            ("F1_25.exe", "F1 25"),     // F1 25
            ("F1Manager*.exe", null),    // F1 Manager games
            ("F1Manager22.exe", "F1 Manager 22"), // F1 Manager 22
            ("F1Manager23.exe", "F1 Manager 23"), // F1 Manager 23
            ("F1Manager24.exe", "F1 Manager 24")  // F1 Manager 24
        };

        public GameDetectionService()
        {
            _configService = App.ConfigService;
            _iconCacheService = App.IconCacheService;
        }

        // Scan for installed F1 games in common locations
        public async Task<List<GameInfo>> ScanForGamesAsync()
        {
            return await Task.Run(() =>
            {
                var results = new List<GameInfo>();
                var removedGamePaths = _configService.GetRemovedGamePaths();

                try
                {
                    App.LogToFile("Beginning game scan");

                    // Reset found games collection for this scan
                    _foundGameExes.Clear();

                    // Check registry for Steam games
                    TryScanSteamRegistry(results, removedGamePaths);

                    // Check registry for Epic/EA/Origin games
                    TryScanRegistryInstalls(results, removedGamePaths);

                    // Scan common installation folders
                    foreach (var path in _commonPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                    {
                        App.LogToFile($"Scanning common path: {path}");
                        ScanDirectoryForGames(path, results, removedGamePaths);
                    }

                    // Scan custom paths from settings
                    var customPaths = _configService.GetCustomGameScanPaths();
                    foreach (var path in customPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                    {
                        App.LogToFile($"Scanning custom path: {path}");
                        ScanDirectoryForGames(path, results, removedGamePaths);
                    }

                    // Add unique entries only (in case there are duplicates)
                    var uniqueResults = results.GroupBy(g => g.ExecutablePath)
                                             .Select(g => g.First())
                                             .ToList();

                    App.LogToFile($"Game scan complete, found {uniqueResults.Count} games");

                    return uniqueResults;
                }
                catch (Exception ex)
                {
                    App.LogToFile($"Error scanning for games: {ex.Message}");
                    return results;
                }
            });
        }

        // Keep track of found games to filter duplicates
        private Dictionary<string, List<string>> _foundGameExes = new Dictionary<string, List<string>>();

        // Scan a specific directory for F1 games
        private void ScanDirectoryForGames(string directory, List<GameInfo> results, List<string> removedGamePaths)
        {
            try
            {
                // Reset found games collection
                _foundGameExes.Clear();

                // First pass: Collect all potential game executables
                CollectGameExecutables(directory, 0, 3);

                // Second pass: Filter and add the preferred executable for each game
                foreach (var gameName in _foundGameExes.Keys)
                {
                    var exePaths = _foundGameExes[gameName];

                    // If we only found one exe for this game, use it
                    if (exePaths.Count == 1)
                    {
                        string exePath = exePaths[0];

                        // Skip if this game was previously removed by the user
                        if (removedGamePaths.Contains(exePath))
                            continue;

                        AddGameToResults(exePath, results);
                    }
                    // Otherwise, choose the preferred executable
                    else if (exePaths.Count > 1)
                    {
                        string preferredExe = ChoosePreferredExecutable(gameName, exePaths);

                        // Skip if this game was previously removed by the user
                        if (removedGamePaths.Contains(preferredExe))
                            continue;

                        AddGameToResults(preferredExe, results);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning directory {directory}: {ex.Message}");
            }
        }

        // Collect all potential game executables in a directory
        private void CollectGameExecutables(string directory, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                // Check all executable files in this directory
                foreach (var file in Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        bool isF1Game = false;
                        string gameName = null;

                        // Check against known patterns
                        foreach (var (pattern, specificGameName) in _knownF1Games)
                        {
                            if (MatchesPattern(fileName, pattern))
                            {
                                isF1Game = true;
                                // Extract base game name without the dx12 suffix
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
                            Debug.WriteLine($"Found game exe: {gameName} - {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking file {file}: {ex.Message}");
                    }
                }

                // Check subdirectories
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    CollectGameExecutables(subDir, currentDepth + 1, maxDepth);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error accessing {directory}: {ex.Message}");
            }
        }

        // Choose the preferred executable when multiple are found
        private string ChoosePreferredExecutable(string gameName, List<string> exePaths)
        {
            // For F1 Manager games
            if (gameName.Contains("Manager"))
            {
                // Prefer the executable in the base directory
                foreach (var exePath in exePaths)
                {
                    string directory = Path.GetDirectoryName(exePath);
                    bool isInWin64 = directory != null &&
                        (directory.Contains("\\Binaries\\Win64") ||
                         directory.Contains("/Binaries/Win64"));

                    if (!isInWin64)
                    {
                        Debug.WriteLine($"Choosing base directory exe for F1 Manager: {exePath}");
                        return exePath;
                    }
                }
            }
            // For regular F1 games
            else
            {
                // Check if we have a non-DX12 version
                string nonDx12Exe = null;
                string dx12Exe = null;

                foreach (var exePath in exePaths)
                {
                    string fileName = Path.GetFileName(exePath);
                    if (fileName.Contains("_dx12"))
                    {
                        dx12Exe = exePath;
                    }
                    else
                    {
                        nonDx12Exe = exePath;
                    }
                }

                // Prefer non-DX12 version if available
                if (nonDx12Exe != null)
                {
                    Debug.WriteLine($"Choosing non-DX12 exe for F1 game: {nonDx12Exe}");
                    return nonDx12Exe;
                }

                // Otherwise use the DX12 version (e.g., for F1 2021 which only has DX12)
                if (dx12Exe != null)
                {
                    Debug.WriteLine($"Only DX12 exe found for F1 game: {dx12Exe}");
                    return dx12Exe;
                }
            }

            // If we couldn't determine a preference, just return the first one
            Debug.WriteLine($"No preferred exe found, using first: {exePaths[0]}");
            return exePaths[0];
        }

        // Add a game to the results list
        private void AddGameToResults(string exePath, List<GameInfo> results)
        {
            try
            {
                string fileName = Path.GetFileName(exePath);
                string gameName = null;

                // Check against known patterns to get the game name
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
                    return; // Not an F1 game
                }

                var gameInfo = new GameInfo
                {
                    ExecutablePath = exePath,
                    InstallDirectory = Path.GetDirectoryName(exePath),
                    Name = gameName,
                    Version = GetFileVersion(exePath),
                    Icon = _iconCacheService.GetIcon(exePath)
                };
                gameInfo.GenerateId(); // Generate the ID after setting properties

                // Ensure we don't add duplicates
                if (!results.Any(g => g.ExecutablePath == gameInfo.ExecutablePath))
                {
                    results.Add(gameInfo);
                    Debug.WriteLine($"Added game to results: {gameInfo.Name} ({gameInfo.ExecutablePath})");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding game {exePath}: {ex.Message}");
            }
        }

        // Recursively scan directories with a maximum depth
        private void ScanDirectoryRecursive(string directory, List<GameInfo> results, int currentDepth, int maxDepth, List<string> removedGamePaths)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                // Check all executable files in this directory
                foreach (var file in Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    // Skip if this game was previously removed by the user
                    if (removedGamePaths.Contains(file))
                    {
                        continue;
                    }

                    CheckIfF1Game(file, results);
                }

                // Check subdirectories
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    ScanDirectoryRecursive(subDir, results, currentDepth + 1, maxDepth, removedGamePaths);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error accessing {directory}: {ex.Message}");
            }
        }

        // Check if an executable file is an F1 game
        private void CheckIfF1Game(string exePath, List<GameInfo> results)
        {
            try
            {
                string fileName = Path.GetFileName(exePath);

                // Check against known patterns
                foreach (var (pattern, gameName) in _knownF1Games)
                {
                    if (MatchesPattern(fileName, pattern))
                    {
                        // It's an F1 game!
                        var gameInfo = new GameInfo
                        {
                            ExecutablePath = exePath,
                            InstallDirectory = Path.GetDirectoryName(exePath),
                            Name = gameName ?? DeriveGameName(fileName),
                            Version = GetFileVersion(exePath),
                            // Use the icon cache service to get the icon
                            Icon = _iconCacheService.GetIcon(exePath)
                        };
                        gameInfo.GenerateId(); // Generate the ID after setting properties

                        // Ensure we don't add duplicates
                        if (!results.Any(g => g.ExecutablePath == gameInfo.ExecutablePath))
                        {
                            results.Add(gameInfo);
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking file {exePath}: {ex.Message}");
            }
        }

        // Try to scan the Steam registry for installed F1 games
        private void TryScanSteamRegistry(List<GameInfo> results, List<string> removedGamePaths)
        {
            try
            {
                App.LogToFile("Scanning Steam registry for games");

                // Steam's registry path that contains installation directory
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            // Convert to Windows path format if needed
                            steamPath = steamPath.Replace("/", "\\");

                            // Check the main Steam library
                            string commonPath = Path.Combine(steamPath, "steamapps", "common");
                            if (Directory.Exists(commonPath))
                            {
                                ScanDirectoryForGames(commonPath, results, removedGamePaths);
                            }

                            // Try to find additional Steam libraries
                            string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                            if (File.Exists(libraryFoldersPath))
                            {
                                // Parse VDF file to find additional library paths
                                foreach (var line in File.ReadAllLines(libraryFoldersPath))
                                {
                                    if (line.Contains("\"path\""))
                                    {
                                        // Extract the path value
                                        int startIndex = line.IndexOf('"', line.IndexOf('"') + 1) + 1;
                                        int endIndex = line.LastIndexOf('"');
                                        if (startIndex > 0 && endIndex > startIndex)
                                        {
                                            string libraryPath = line.Substring(startIndex, endIndex - startIndex)
                                                .Replace("\\\\", "\\"); // Fix escaped backslashes

                                            string libCommonPath = Path.Combine(libraryPath, "steamapps", "common");
                                            if (Directory.Exists(libCommonPath))
                                            {
                                                ScanDirectoryForGames(libCommonPath, results, removedGamePaths);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error scanning Steam registry: {ex.Message}");
            }
        }

        // Try to scan other registry entries for game installations
        private void TryScanRegistryInstalls(List<GameInfo> results, List<string> removedGamePaths)
        {
            try
            {
                App.LogToFile("Scanning Windows registry for game installations");

                // This approach checks the Windows uninstall registry locations
                var registryKeys = new[]
                {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

                foreach (var baseKeyPath in registryKeys)
                {
                    using (var baseKey = Registry.LocalMachine.OpenSubKey(baseKeyPath))
                    {
                        if (baseKey == null) continue;

                        foreach (var subKeyName in baseKey.GetSubKeyNames())
                        {
                            using (var subKey = baseKey.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                // Check if this is an F1 game
                                string displayName = subKey.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(displayName) &&
                                    (displayName.Contains("F1") || displayName.Contains("Formula 1")))
                                {
                                    string installLocation = subKey.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                    {
                                        ScanDirectoryForGames(installLocation, results, removedGamePaths);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error scanning registry installations: {ex.Message}");
            }
        }

        // Simple pattern matching for executables (supports wildcards)
        private bool MatchesPattern(string filename, string pattern)
        {
            // Convert the pattern to a regex pattern
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(filename, regexPattern, RegexOptions.IgnoreCase);
        }

        // Extract a game name from the executable if not explicitly known
        private string DeriveGameName(string fileName)
        {
            // Remove extension
            string name = Path.GetFileNameWithoutExtension(fileName);

            // Clean up by replacing underscores with spaces
            name = name.Replace('_', ' ');

            // Handle F1 20XX naming format (keep as is, these are already F1 2021, F1 2020, etc.)
            if (name.StartsWith("F1 20") || name.Contains("F1 20"))
            {
                int startIndex = name.IndexOf("20");
                if (startIndex >= 0 && startIndex + 4 <= name.Length)
                {
                    return $"F1 {name.Substring(startIndex, 4)}";
                }
            }

            // Handle F1XX format (like F122, F123)
            if (name.StartsWith("F1") && name.Length >= 4 && char.IsDigit(name[2]) && char.IsDigit(name[3]))
            {
                // Extract the year digits
                string yearDigits = name.Substring(2, 2);
                int year = int.TryParse(yearDigits, out int y) ? y : 0;

                // For F1 22 and newer (years >= 22), use F1 XX format instead of F1 20XX
                if (year >= 22)
                {
                    return $"F1 {yearDigits}";
                }
                else
                {
                    // Keep old behavior for older games
                    return $"F1 20{yearDigits}";
                }
            }

            // Handle F1 Manager
            if (name.Contains("Manager"))
            {
                // Try to extract year
                var match = Regex.Match(name, @"(\d{2,4})");
                if (match.Success)
                {
                    string year = match.Groups[1].Value;
                    return $"F1 Manager {year}";
                }
                return "F1 Manager";
            }

            // Make sure the first letter of each word is capitalized
            // Split by spaces, capitalize first letter of each word, then join back
            string[] parts = name.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    parts[i] = char.ToUpper(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1) : "");
                }
            }

            return string.Join(" ", parts);
        }

        // Get the file version from an executable
        private string GetFileVersion(string filePath)
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                {
                    return versionInfo.FileVersion;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting file version for {filePath}: {ex.Message}");
            }

            return "Unknown";
        }

        // Allow user to manually add a game by selecting the executable
        public GameInfo AddGameManually(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                return null;

            try
            {
                string fileName = Path.GetFileName(executablePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(executablePath);

                // Format the name nicely for display (replace underscores with spaces)
                string displayName = fileNameWithoutExt.Replace('_', ' ');

                // Check if it matches any known F1 game patterns
                string derivedName = null;
                foreach (var (pattern, gameName) in _knownF1Games)
                {
                    if (MatchesPattern(fileName, pattern) && !string.IsNullOrEmpty(gameName))
                    {
                        derivedName = gameName;
                        break;
                    }
                }

                // If no match found, derive the name using our algorithm
                if (string.IsNullOrEmpty(derivedName))
                {
                    derivedName = DeriveGameName(fileName);
                }

                // Get the icon for the executable using the icon cache
                BitmapImage icon = _iconCacheService.GetIcon(executablePath);

                // Create the game info object
                var gameInfo = new GameInfo
                {
                    ExecutablePath = executablePath,
                    InstallDirectory = Path.GetDirectoryName(executablePath),
                    Name = derivedName,
                    Version = GetFileVersion(executablePath),
                    Icon = icon,
                    IsManuallyAdded = true // Mark as manually added
                };
                gameInfo.GenerateId(); // Generate the ID after setting properties

                // If this game was previously removed, remove it from the removed list
                _configService.RemoveFromRemovedGames(executablePath);

                return gameInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding game manually: {ex.Message}");
                return null;
            }
        }

        // Check if a previously saved game still exists
        public bool GameExistsOnDisk(string executablePath)
        {
            return !string.IsNullOrEmpty(executablePath) && File.Exists(executablePath);
        }
    }
}
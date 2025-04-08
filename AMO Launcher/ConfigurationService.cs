using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using AMO_Launcher.Models;

namespace AMO_Launcher.Services
{
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        private readonly string _iconCacheFolderPath;
        private AppSettings _currentSettings;

        public ConfigurationService()
        {
            // Store config in AppData
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            // Create directory if it doesn't exist
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            // Create icons folder if it doesn't exist
            _iconCacheFolderPath = Path.Combine(appDataPath, "IconCache");
            if (!Directory.Exists(_iconCacheFolderPath))
            {
                Directory.CreateDirectory(_iconCacheFolderPath);
            }

            _configFilePath = Path.Combine(appDataPath, "settings.json");

            // Initialize with default settings
            _currentSettings = new AppSettings
            {
                Games = new List<GameSetting>(),
                RemovedGamePaths = new List<string>(), // Track removed games
                DefaultGameId = null,
                LastGameId = null,
                AppliedMods = new Dictionary<string, List<AppliedModSetting>>(),
                LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>(),
                // Use case-insensitive dictionary to prevent key mismatches
                GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase),
                ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        // Load the settings from disk
        public async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = await File.ReadAllTextAsync(_configFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                    // Ensure the RemovedGamePaths list exists
                    if (loadedSettings.RemovedGamePaths == null)
                    {
                        loadedSettings.RemovedGamePaths = new List<string>();
                    }

                    // Ensure the AppliedMods dictionary exists
                    if (loadedSettings.AppliedMods == null)
                    {
                        loadedSettings.AppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                    }

                    // Ensure the LastAppliedMods dictionary exists
                    if (loadedSettings.LastAppliedMods == null)
                    {
                        loadedSettings.LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                    }

                    // Ensure the GameProfiles dictionary exists and is case-insensitive
                    if (loadedSettings.GameProfiles == null)
                    {
                        loadedSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                    }
                    else if (!(loadedSettings.GameProfiles is Dictionary<string, List<ModProfile>>))
                    {
                        // Copy to a case-insensitive dictionary if it's not already one
                        var newDict = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var entry in loadedSettings.GameProfiles)
                        {
                            newDict[entry.Key] = entry.Value;
                        }
                        loadedSettings.GameProfiles = newDict;
                    }

                    // Ensure the ActiveProfileIds dictionary exists and is case-insensitive
                    if (loadedSettings.ActiveProfileIds == null)
                    {
                        loadedSettings.ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else if (!(loadedSettings.ActiveProfileIds is Dictionary<string, string>))
                    {
                        // Copy to a case-insensitive dictionary if it's not already one
                        var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var entry in loadedSettings.ActiveProfileIds)
                        {
                            newDict[entry.Key] = entry.Value;
                        }
                        loadedSettings.ActiveProfileIds = newDict;
                    }

                    _currentSettings = loadedSettings;

                    // Load icons from disk for all games
                    foreach (var game in _currentSettings.Games)
                    {
                        if (game.IsManuallyAdded && !string.IsNullOrEmpty(game.ExecutablePath))
                        {
                            string iconPath = GetIconFilePath(game.ExecutablePath);
                            if (File.Exists(iconPath))
                            {
                                try
                                {
                                    LoadAndCacheIconFromDisk(game.ExecutablePath);
                                }
                                catch (Exception ex)
                                {
                                    App.LogToFile($"Error loading icon for {game.ExecutablePath}: {ex.Message}");
                                }
                            }
                        }
                    }

                    return _currentSettings;
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue with default settings
                App.LogToFile($"Error loading settings: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");

                // Try to load from backup if main file failed
                return await TryLoadFromBackupAsync();
            }

            return _currentSettings;
        }

        // Save the current settings to disk
        public async Task SaveSettingsAsync()
        {
            try
            {
                string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_configFilePath, json);

                // Save icons for manually added games
                foreach (var game in _currentSettings.Games)
                {
                    if (game.IsManuallyAdded && !string.IsNullOrEmpty(game.ExecutablePath))
                    {
                        // Try to get the icon from the cache and save it
                        var icon = App.IconCacheService.GetIcon(game.ExecutablePath);
                        if (icon != null)
                        {
                            SaveIconToDisk(game.ExecutablePath, icon);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // Update the games list
        public void UpdateGames(List<GameInfo> games)
        {
            // Convert GameInfo objects to GameSetting objects for storage
            _currentSettings.Games = new List<GameSetting>();

            foreach (var game in games)
            {
                _currentSettings.Games.Add(new GameSetting
                {
                    Id = game.Id,
                    Name = game.Name,
                    ExecutablePath = game.ExecutablePath,
                    InstallDirectory = game.InstallDirectory ?? Path.GetDirectoryName(game.ExecutablePath),
                    IsDefault = game.IsDefault,
                    IsManuallyAdded = game.IsManuallyAdded
                });

                // If this is marked as default, update the default game ID
                if (game.IsDefault)
                {
                    _currentSettings.DefaultGameId = game.Id;
                }
            }
        }

        // Generate a path for storing the icon on disk
        private string GetIconFilePath(string executablePath)
        {
            string safeName = GetSafeFileName(executablePath);
            return Path.Combine(_iconCacheFolderPath, $"{safeName}.png");
        }

        // Convert a path to a safe filename
        private string GetSafeFileName(string path)
        {
            // Create a hash of the path to use as a unique filename
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(path);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        // Save an icon to disk
        private void SaveIconToDisk(string executablePath, BitmapImage icon)
        {
            try
            {
                string iconPath = GetIconFilePath(executablePath);

                // Convert BitmapImage to a PNG file
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));

                using (var fileStream = new FileStream(iconPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving icon to disk: {ex.Message}");
            }
        }

        // Load an icon from disk and cache it
        private void LoadAndCacheIconFromDisk(string executablePath)
        {
            try
            {
                string iconPath = GetIconFilePath(executablePath);
                if (File.Exists(iconPath))
                {
                    var icon = new BitmapImage();
                    icon.BeginInit();
                    icon.CacheOption = BitmapCacheOption.OnLoad;
                    icon.UriSource = new Uri(iconPath, UriKind.Absolute);
                    icon.EndInit();
                    icon.Freeze(); // Make it thread-safe

                    // Add to the application-wide cache
                    App.IconCacheService.AddToCache(executablePath, icon);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading icon from disk: {ex.Message}");
            }
        }

        // Add a game to the removed list
        public void AddToRemovedGames(string executablePath)
        {
            if (!string.IsNullOrEmpty(executablePath) &&
                !_currentSettings.RemovedGamePaths.Contains(executablePath))
            {
                _currentSettings.RemovedGamePaths.Add(executablePath);
            }
        }

        // Check if a game is in the removed list
        public bool IsGameRemoved(string executablePath)
        {
            return !string.IsNullOrEmpty(executablePath) &&
                   _currentSettings.RemovedGamePaths.Contains(executablePath);
        }

        // Remove a game from the removed list (if the user wants to add it back)
        public void RemoveFromRemovedGames(string executablePath)
        {
            if (!string.IsNullOrEmpty(executablePath))
            {
                _currentSettings.RemovedGamePaths.Remove(executablePath);
            }
        }

        // Set a game as the default
        public void SetDefaultGame(string gameId)
        {
            _currentSettings.DefaultGameId = gameId;

            // If gameId is null, clear default
            if (gameId == null)
            {
                foreach (var game in _currentSettings.Games)
                {
                    game.IsDefault = false;
                }
                return;
            }

            // Update the IsDefault flag on all games
            foreach (var game in _currentSettings.Games)
            {
                game.IsDefault = game.Id == gameId;
            }
        }

        // Set the last selected game
        public void SetLastSelectedGame(string gameId)
        {
            _currentSettings.LastGameId = gameId;
        }

        // Get the default or last used game ID
        public string GetPreferredGameId()
        {
            // Prefer default game if set
            if (!string.IsNullOrEmpty(_currentSettings.DefaultGameId))
            {
                return _currentSettings.DefaultGameId;
            }

            // Fall back to last selected game
            return _currentSettings.LastGameId;
        }

        // Get the list of removed game paths
        public List<string> GetRemovedGamePaths()
        {
            return _currentSettings.RemovedGamePaths ?? new List<string>();
        }

        // Save applied mods for a specific game
        public async Task SaveAppliedModsAsync(string gameId, List<AppliedModSetting> appliedMods)
        {
            if (string.IsNullOrEmpty(gameId)) return;

            _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
            _currentSettings.AppliedMods[gameId] = appliedMods;

            // Save settings asynchronously
            await SaveSettingsAsync();
        }

        // Non-async wrapper for backwards compatibility
        public void SaveAppliedMods(string gameId, List<AppliedModSetting> appliedMods)
        {
            if (string.IsNullOrEmpty(gameId)) return;

            _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
            _currentSettings.AppliedMods[gameId] = appliedMods;

            // Use Task.Run to prevent blocking
            Task.Run(() => SaveSettingsAsync()).ConfigureAwait(false);
        }

        // Save the last applied mods state
        public async Task SaveLastAppliedModsStateAsync(string gameId, List<AppliedModSetting> appliedMods)
        {
            if (string.IsNullOrEmpty(gameId)) return;

            _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
            _currentSettings.LastAppliedMods[gameId] = appliedMods;

            // Save settings asynchronously
            await SaveSettingsAsync();
        }

        // Non-async wrapper
        public void SaveLastAppliedModsState(string gameId, List<AppliedModSetting> appliedMods)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                System.Diagnostics.Debug.WriteLine("SaveLastAppliedModsState: gameId is null!");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"SaveLastAppliedModsState: Saving {appliedMods.Count} mods for game {gameId}");

            _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
            _currentSettings.LastAppliedMods[gameId] = appliedMods;

            // DON'T use Task.Run - this creates a fire-and-forget operation that's not tracked
            // Instead, use a synchronous save to ensure it completes
            try
            {
                string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configFilePath, json);
                System.Diagnostics.Debug.WriteLine("Settings saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // Get applied mods for a specific game
        public List<AppliedModSetting> GetAppliedMods(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return new List<AppliedModSetting>();

            _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();

            if (_currentSettings.AppliedMods.TryGetValue(gameId, out var mods))
            {
                return mods;
            }

            return new List<AppliedModSetting>();
        }

        // Get the last applied mods state
        public List<AppliedModSetting> GetLastAppliedModsState(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                System.Diagnostics.Debug.WriteLine($"GetLastAppliedModsState: gameId is null or empty");
                return null;
            }

            _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();

            if (_currentSettings.LastAppliedMods.TryGetValue(gameId, out var mods))
            {
                System.Diagnostics.Debug.WriteLine($"GetLastAppliedModsState: Found {mods.Count} mods for game {gameId}");
                return mods;
            }

            System.Diagnostics.Debug.WriteLine($"GetLastAppliedModsState: No mods found for game {gameId}");
            return null;
        }

        // Flag to track if mods have been changed
        private bool _modsChanged = false;

        // Mark mods as changed
        public void MarkModsChanged()
        {
            _modsChanged = true;
        }

        // Reset the mods changed flag
        public void ResetModsChangedFlag()
        {
            _modsChanged = false;
        }

        // Check if mods have changed since last application
        public bool HaveModsChanged(string gameId, List<AppliedModSetting> currentActiveMods)
        {
            // Debug which condition is triggering
            if (_modsChanged)
            {
                System.Diagnostics.Debug.WriteLine("Mods changed because flag is set");
                return true;
            }

            var lastAppliedState = GetLastAppliedModsState(gameId);
            // If no last state exists at all, then mods have changed
            if (lastAppliedState == null)
            {
                System.Diagnostics.Debug.WriteLine("Mods changed because last state is null");
                return true;
            }

            // If both current and last state have 0 mods, nothing changed
            if (lastAppliedState.Count == 0 && currentActiveMods.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No changes: both have 0 mods");
                return false;
            }

            // If counts differ, something changed
            if (lastAppliedState.Count != currentActiveMods.Count)
            {
                return true;
            }

            // If count is different, mods have changed
            if (currentActiveMods.Count != lastAppliedState.Count)
                return true;

            // Check if each mod in current state matches last state
            for (int i = 0; i < currentActiveMods.Count; i++)
            {
                var currentMod = currentActiveMods[i];
                var lastMod = lastAppliedState[i];

                // If different mod or different order, mods have changed
                if (currentMod.ModFolderPath != lastMod.ModFolderPath ||
                    currentMod.IsFromArchive != lastMod.IsFromArchive ||
                    currentMod.ArchiveSource != lastMod.ArchiveSource ||
                    currentMod.ArchiveRootPath != lastMod.ArchiveRootPath)
                {
                    return true;
                }
            }

            // No differences found
            return false;
        }

        public async Task SaveProfilesAsync(string gameId, List<ModProfile> profiles)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || profiles == null)
                    return;

                // Make sure settings object exists
                if (_currentSettings == null)
                    await LoadSettingsAsync();

                // Ensure the game profiles dictionary exists
                if (_currentSettings.GameProfiles == null)
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);

                // Create a backup of the profiles before saving
                BackupSettingsFile();

                // Filter out any null profiles
                var validProfiles = profiles.Where(p => p != null).ToList();
                if (validProfiles.Count < profiles.Count)
                {
                    App.LogToFile($"Filtered out {profiles.Count - validProfiles.Count} null profiles");
                }

                // Save the profiles
                _currentSettings.GameProfiles[gameId] = validProfiles;

                // Save settings to disk
                await SaveSettingsAsync();

                App.LogToFile($"Saved {profiles.Count} profiles for game {gameId}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving profiles: {ex.Message}");
            }
        }

        public async Task SaveActiveProfileIdAsync(string gameId, string profileId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                    return;

                // Make sure settings object exists
                if (_currentSettings == null)
                    await LoadSettingsAsync();

                // Ensure the active profiles dictionary exists
                if (_currentSettings.ActiveProfileIds == null)
                    _currentSettings.ActiveProfileIds = new Dictionary<string, string>();

                // Save the active profile ID
                _currentSettings.ActiveProfileIds[gameId] = profileId;

                // Save settings to disk
                await SaveSettingsAsync();

                App.LogToFile($"Saved active profile ID {profileId} for game {gameId}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving active profile ID: {ex.Message}");
            }
        }

        public async Task<List<string>> GetGameIdsWithProfilesAsync()
        {
            try
            {
                // Make sure settings object exists
                if (_currentSettings == null)
                    await LoadSettingsAsync();

                // Return empty list if no profiles exist
                if (_currentSettings.GameProfiles == null)
                    return new List<string>();

                // Return all game IDs with profiles
                return new List<string>(_currentSettings.GameProfiles.Keys);
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error getting game IDs with profiles: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<List<ModProfile>> LoadProfilesAsync(string gameId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                    return new List<ModProfile>();

                App.LogToFile($"LoadProfilesAsync: Loading profiles for game {gameId}");

                // Make sure settings object exists
                if (_currentSettings == null)
                    await LoadSettingsAsync();

                // Initialize if needed
                if (_currentSettings.GameProfiles == null)
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>();

                // Log all available game IDs for debugging
                App.LogToFile("Available game IDs in settings:");
                foreach (var id in _currentSettings.GameProfiles.Keys)
                {
                    App.LogToFile($"  - {id}");
                }

                // Find the active profile
                if (!_currentSettings.GameProfiles.TryGetValue(gameId, out var profiles))
                {
                    // Enhanced fuzzy matching - try just the game name
                    string gameName = gameId.Split('_')[0];

                    // Try to find any key that starts with the game name
                    var matchingKey = _currentSettings.GameProfiles.Keys
                        .FirstOrDefault(k => k.StartsWith(gameName, StringComparison.OrdinalIgnoreCase));

                    if (matchingKey != null)
                    {
                        App.LogToFile($"Found profiles using loose match: {matchingKey}");
                        profiles = _currentSettings.GameProfiles[matchingKey];

                        // Store with new ID to avoid future mismatches
                        _currentSettings.GameProfiles[gameId] = profiles;
                        await SaveSettingsAsync();
                    }
                }

                // If no profiles found at all, create a new list (but don't save it yet)
                App.LogToFile($"No profiles found for game {gameId}, returning empty list");
                return new List<ModProfile>();
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in LoadProfilesAsync: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return new List<ModProfile>();
            }
        }

        public async Task<string> LoadActiveProfileIdAsync(string gameId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                    return null;

                // Make sure settings object exists
                if (_currentSettings == null)
                    await LoadSettingsAsync();

                // Return null if no active profile ID exists
                if (_currentSettings.ActiveProfileIds == null ||
                    !_currentSettings.ActiveProfileIds.ContainsKey(gameId))
                    return null;

                // Return the active profile ID
                return _currentSettings.ActiveProfileIds[gameId];
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading active profile ID: {ex.Message}");
                return null;
            }
        }

        public List<ModProfile> GetProfiles(string gameId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                    return new List<ModProfile>();

                // Log the exact game ID we're looking for
                App.LogToFile($"GetProfiles: Looking for profiles with game ID: {gameId}");

                // Ensure dictionaries are initialized
                if (_currentSettings.GameProfiles == null)
                {
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                    App.LogToFile("Created new GameProfiles dictionary");
                }

                // Log all available game IDs in GameProfiles for debugging
                foreach (var key in _currentSettings.GameProfiles.Keys)
                {
                    App.LogToFile($"Available profile game ID: {key}");
                }

                // Check if there are profiles for this exact game ID
                if (_currentSettings.GameProfiles.TryGetValue(gameId, out var exactProfiles))
                {
                    App.LogToFile($"Found {exactProfiles.Count} profiles with exact ID match");
                    return exactProfiles;
                }

                // If not found with the exact ID, try to match by game name portion
                string gameName = gameId.Split('_')[0]; // Extract name part before underscore

                foreach (var entry in _currentSettings.GameProfiles)
                {
                    // Check if the key starts with the game name
                    if (entry.Key.StartsWith(gameName + "_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.Key, gameName, StringComparison.OrdinalIgnoreCase))
                    {
                        App.LogToFile($"Found profiles using partial name match: {entry.Key}");

                        // Save with new ID to prevent future mismatches
                        _currentSettings.GameProfiles[gameId] = entry.Value;
                        Task.Run(() => SaveSettingsAsync());

                        return entry.Value;
                    }
                }

                // No profiles found - create a default one
                var defaultProfile = new ModProfile();
                var newList = new List<ModProfile> { defaultProfile };
                _currentSettings.GameProfiles[gameId] = newList;
                Task.Run(() => SaveSettingsAsync());

                App.LogToFile($"No profiles found for this game, created default profile");
                return newList;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in GetProfiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                // Return a new default profile in case of error
                return new List<ModProfile> { new ModProfile() };
            }
        }

        private void BackupSettingsFile()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                    return;

                string backupPath = _configFilePath + ".bak";
                File.Copy(_configFilePath, backupPath, true);
                App.LogToFile($"Settings file backed up to {backupPath}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error backing up settings file: {ex.Message}");
            }
        }

        private async Task<AppSettings> TryLoadFromBackupAsync()
        {
            try
            {
                string backupPath = _configFilePath + ".bak";
                if (File.Exists(backupPath))
                {
                    App.LogToFile("Attempting to load settings from backup file");
                    string json = await File.ReadAllTextAsync(backupPath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                    // Initialize required collections
                    if (loadedSettings.RemovedGamePaths == null)
                        loadedSettings.RemovedGamePaths = new List<string>();

                    if (loadedSettings.AppliedMods == null)
                        loadedSettings.AppliedMods = new Dictionary<string, List<AppliedModSetting>>();

                    if (loadedSettings.LastAppliedMods == null)
                        loadedSettings.LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>();

                    if (loadedSettings.GameProfiles == null)
                        loadedSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);

                    if (loadedSettings.ActiveProfileIds == null)
                        loadedSettings.ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    _currentSettings = loadedSettings;
                    App.LogToFile("Successfully loaded settings from backup file");

                    // Save to main file to repair it
                    await SaveSettingsAsync();

                    return _currentSettings;
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading settings from backup: {ex.Message}");
            }

            return _currentSettings;
        }
    }

    // Classes for serialization
    public class AppSettings
    {
        public List<GameSetting> Games { get; set; }
        public List<string> RemovedGamePaths { get; set; } = new List<string>(); // Track removed game paths
        public string DefaultGameId { get; set; }
        public string LastGameId { get; set; }
        public Dictionary<string, List<AppliedModSetting>> AppliedMods { get; set; } = new Dictionary<string, List<AppliedModSetting>>();
        public Dictionary<string, List<AppliedModSetting>> LastAppliedMods { get; set; } = new Dictionary<string, List<AppliedModSetting>>();
        public Dictionary<string, List<ModProfile>> GameProfiles { get; set; } = new Dictionary<string, List<ModProfile>>();
        public Dictionary<string, string> ActiveProfileIds { get; set; } = new Dictionary<string, string>();
    }

    public class GameSetting
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public string InstallDirectory { get; set; }
        public bool IsDefault { get; set; }
        public bool IsManuallyAdded { get; set; } // Track manually added games
    }
}
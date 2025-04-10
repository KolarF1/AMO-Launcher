using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Linq;
using AMO_Launcher.Models;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Services
{
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        private readonly string _iconCacheFolderPath;
        private AppSettings _currentSettings;
        private static SemaphoreSlim _fileSemaphore = new SemaphoreSlim(1, 1);

        public ConfigurationService()
        {
            try
            {
                App.LogService?.LogDebug("Initializing ConfigurationService");

                // Store config in AppData
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                // Create directory if it doesn't exist
                if (!Directory.Exists(appDataPath))
                {
                    App.LogService?.LogDebug($"Creating application data directory: {appDataPath}");
                    Directory.CreateDirectory(appDataPath);
                }

                // Create icons folder if it doesn't exist
                _iconCacheFolderPath = Path.Combine(appDataPath, "IconCache");
                if (!Directory.Exists(_iconCacheFolderPath))
                {
                    App.LogService?.LogDebug($"Creating icon cache directory: {_iconCacheFolderPath}");
                    Directory.CreateDirectory(_iconCacheFolderPath);
                }

                _configFilePath = Path.Combine(appDataPath, "settings.json");
                App.LogService?.LogDebug($"Config file path set to: {_configFilePath}");

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

                App.LogService?.Info("ConfigurationService initialized successfully");
            }
            catch (Exception ex)
            {
                App.LogService?.Error($"Failed to initialize ConfigurationService: {ex.Message}");
                App.LogService?.LogDebug($"Exception details: {ex}");
                throw; // Rethrow to let the application handle a critical initialization failure
            }
        }

        // Load the settings from disk
        public async Task<AppSettings> LoadSettingsAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Loading application settings");

                if (File.Exists(_configFilePath))
                {
                    App.LogService?.LogDebug($"Reading settings from: {_configFilePath}");
                    try
                    {
                        string json = await File.ReadAllTextAsync(_configFilePath);

                        var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                        App.LogService?.LogDebug("Settings deserialized successfully");

                        // Ensure the RemovedGamePaths list exists
                        if (loadedSettings.RemovedGamePaths == null)
                        {
                            App.LogService?.LogDebug("Initializing missing RemovedGamePaths collection");
                            loadedSettings.RemovedGamePaths = new List<string>();
                        }

                        // Ensure the AppliedMods dictionary exists
                        if (loadedSettings.AppliedMods == null)
                        {
                            App.LogService?.LogDebug("Initializing missing AppliedMods dictionary");
                            loadedSettings.AppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                        }

                        // Ensure the LastAppliedMods dictionary exists
                        if (loadedSettings.LastAppliedMods == null)
                        {
                            App.LogService?.LogDebug("Initializing missing LastAppliedMods dictionary");
                            loadedSettings.LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                        }

                        // Ensure the GameProfiles dictionary exists and is case-insensitive
                        if (loadedSettings.GameProfiles == null)
                        {
                            App.LogService?.LogDebug("Initializing missing GameProfiles dictionary");
                            loadedSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                        }
                        else if (!(loadedSettings.GameProfiles is Dictionary<string, List<ModProfile>>))
                        {
                            // Copy to a case-insensitive dictionary if it's not already one
                            App.LogService?.LogDebug("Converting GameProfiles to case-insensitive dictionary");
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
                            App.LogService?.LogDebug("Initializing missing ActiveProfileIds dictionary");
                            loadedSettings.ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                        else if (!(loadedSettings.ActiveProfileIds is Dictionary<string, string>))
                        {
                            // Copy to a case-insensitive dictionary if it's not already one
                            App.LogService?.LogDebug("Converting ActiveProfileIds to case-insensitive dictionary");
                            var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var entry in loadedSettings.ActiveProfileIds)
                            {
                                newDict[entry.Key] = entry.Value;
                            }
                            loadedSettings.ActiveProfileIds = newDict;
                        }

                        _currentSettings = loadedSettings;
                        App.LogService?.Info("Settings loaded successfully");

                        // Load icons from disk for all games
                        App.LogService?.LogDebug("Loading icons for manually added games");
                        int iconCount = 0;
                        int failedIcons = 0;

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
                                        iconCount++;
                                        App.LogService?.Trace($"Loaded icon for game: {game.Name} ({game.ExecutablePath})");
                                    }
                                    catch (Exception ex)
                                    {
                                        failedIcons++;
                                        App.LogService?.Warning($"Error loading icon for {game.ExecutablePath}: {ex.Message}");
                                        App.LogService?.LogDebug($"Icon load error details: {ex}");
                                    }
                                }
                            }
                        }

                        App.LogService?.LogDebug($"Icon loading complete - loaded {iconCount} icons, {failedIcons} failed");

                        return _currentSettings;
                    }
                    catch (Exception ex)
                    {
                        App.LogService?.Error($"Error reading settings file: {ex.Message}");
                        App.LogService?.LogDebug($"Will attempt to load from backup or create new settings file");

                        // Try loading from backup first
                        bool backupLoaded = false;
                        try
                        {
                            var backupSettings = await TryLoadFromBackupAsync();
                            if (backupSettings != null)
                            {
                                backupLoaded = true;
                                return backupSettings;
                            }
                        }
                        catch (Exception backupEx)
                        {
                            App.LogService?.Error($"Failed to load from backup: {backupEx.Message}");
                        }

                        // If backup load failed, create new settings file
                        if (!backupLoaded)
                        {
                            App.LogService?.Info("Creating new settings file with defaults");
                            await SaveSettingsAsync(); // Save current default settings
                        }
                    }
                }
                else
                {
                    App.LogService?.Info("Settings file not found, creating with default settings");
                    await SaveSettingsAsync(); // Save current default settings to create the file
                }

                return _currentSettings;
            }, "LoadSettingsAsync", true, _currentSettings);
        }

        // Save the current settings to disk
        public async Task SaveSettingsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var startTime = DateTime.Now;
                App.LogService?.LogDebug("Saving application settings");

                try
                {
                    // Wait to acquire the semaphore
                    await _fileSemaphore.WaitAsync();

                    // Create a backup before saving
                    BackupSettingsFile();

                    string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    // Use FileShare.Read to allow reading but prevent other writes
                    using (var fileStream = new FileStream(_configFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(fileStream))
                    {
                        await writer.WriteAsync(json);
                        await writer.FlushAsync();
                    }

                    App.LogService?.LogDebug($"Settings saved to: {_configFilePath}");

                    // Save icons for manually added games
                    int iconCount = 0;
                    int failedIcons = 0;

                    App.LogService?.LogDebug("Saving icons for manually added games");
                    foreach (var game in _currentSettings.Games)
                    {
                        if (game.IsManuallyAdded && !string.IsNullOrEmpty(game.ExecutablePath))
                        {
                            try
                            {
                                // Try to get the icon from the cache and save it
                                var icon = App.IconCacheService.GetIcon(game.ExecutablePath);
                                if (icon != null)
                                {
                                    SaveIconToDisk(game.ExecutablePath, icon);
                                    iconCount++;
                                    App.LogService?.Trace($"Saved icon for game: {game.Name} ({game.ExecutablePath})");
                                }
                            }
                            catch (Exception ex)
                            {
                                failedIcons++;
                                App.LogService?.Warning($"Error saving icon for {game.ExecutablePath}: {ex.Message}");
                                App.LogService?.LogDebug($"Icon save error details: {ex}");
                            }
                        }
                    }

                    App.LogService?.LogDebug($"Icon saving complete - saved {iconCount} icons, {failedIcons} failed");

                    // Calculate elapsed time
                    var elapsed = DateTime.Now - startTime;
                    App.LogService?.LogDebug($"SaveSettingsAsync completed in {elapsed.TotalMilliseconds:F1}ms");

                    App.LogService?.Info("Settings saved successfully");
                }
                finally
                {
                    // Always release the semaphore
                    _fileSemaphore.Release();
                }
            }, "SaveSettingsAsync");
        }

        // Update the games list
        public void UpdateGames(List<GameInfo> games)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (games == null)
                {
                    App.LogService?.Warning("UpdateGames called with null games list");
                    return;
                }

                App.LogService?.Info($"Updating games list with {games.Count} games");

                // Convert GameInfo objects to GameSetting objects for storage
                _currentSettings.Games = new List<GameSetting>();
                string defaultGameId = null;

                foreach (var game in games)
                {
                    if (game == null)
                    {
                        App.LogService?.Warning("Null game encountered in UpdateGames");
                        continue;
                    }

                    App.LogService?.LogDebug($"Processing game: {game.Name} ({game.Id})");

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
                        defaultGameId = game.Id;
                        App.LogService?.LogDebug($"Setting {game.Name} as default game");
                    }
                }

                _currentSettings.DefaultGameId = defaultGameId;
                App.LogService?.Info($"Games list updated successfully with {_currentSettings.Games.Count} games");
            }, "UpdateGames");
        }

        // Generate a path for storing the icon on disk
        private string GetIconFilePath(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.Warning("GetIconFilePath called with null or empty executablePath");
                    return null;
                }

                string safeName = GetSafeFileName(executablePath);
                string iconPath = Path.Combine(_iconCacheFolderPath, $"{safeName}.png");

                App.LogService?.Trace($"Icon file path for {executablePath}: {iconPath}");
                return iconPath;
            }, "GetIconFilePath", false, null);
        }

        // Convert a path to a safe filename
        private string GetSafeFileName(string path)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    App.LogService?.Warning("GetSafeFileName called with null or empty path");
                    return "unknown";
                }

                // Create a hash of the path to use as a unique filename
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(path);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);
                    string result = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    App.LogService?.Trace($"Generated safe filename for {path}: {result}");
                    return result;
                }
            }, "GetSafeFileName", false, "unknown");
        }

        // Save an icon to disk
        private void SaveIconToDisk(string executablePath, BitmapImage icon)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.Warning("SaveIconToDisk called with null or empty executablePath");
                    return;
                }

                if (icon == null)
                {
                    App.LogService?.Warning($"SaveIconToDisk called with null icon for {executablePath}");
                    return;
                }

                string iconPath = GetIconFilePath(executablePath);
                App.LogService?.LogDebug($"Saving icon for {executablePath} to {iconPath}");

                // Convert BitmapImage to a PNG file
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));

                using (var fileStream = new FileStream(iconPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                App.LogService?.LogDebug("Icon saved successfully");
            }, "SaveIconToDisk");
        }

        // Load an icon from disk and cache it
        private void LoadAndCacheIconFromDisk(string executablePath)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.Warning("LoadAndCacheIconFromDisk called with null or empty executablePath");
                    return;
                }

                string iconPath = GetIconFilePath(executablePath);

                if (!File.Exists(iconPath))
                {
                    App.LogService?.LogDebug($"Icon file not found at {iconPath}");
                    return;
                }

                App.LogService?.LogDebug($"Loading icon from {iconPath}");

                var icon = new BitmapImage();
                icon.BeginInit();
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.UriSource = new Uri(iconPath, UriKind.Absolute);
                icon.EndInit();
                icon.Freeze(); // Make it thread-safe

                // Add to the application-wide cache
                App.IconCacheService.AddToCache(executablePath, icon);
                App.LogService?.LogDebug("Icon loaded and cached successfully");
            }, "LoadAndCacheIconFromDisk");
        }

        // Add a game to the removed list
        public void AddToRemovedGames(string executablePath)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.Warning("AddToRemovedGames called with null or empty executablePath");
                    return;
                }

                if (!_currentSettings.RemovedGamePaths.Contains(executablePath))
                {
                    _currentSettings.RemovedGamePaths.Add(executablePath);
                    App.LogService?.Info($"Added game to removed list: {executablePath}");
                }
                else
                {
                    App.LogService?.LogDebug($"Game already in removed list: {executablePath}");
                }
            }, "AddToRemovedGames");
        }

        // Check if a game is in the removed list
        public bool IsGameRemoved(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.LogDebug("IsGameRemoved called with null or empty executablePath");
                    return false;
                }

                bool isRemoved = _currentSettings.RemovedGamePaths.Contains(executablePath);
                App.LogService?.Trace($"Checking if game is removed: {executablePath}, result: {isRemoved}");
                return isRemoved;
            }, "IsGameRemoved", false, false);
        }

        // Remove a game from the removed list (if the user wants to add it back)
        public void RemoveFromRemovedGames(string executablePath)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService?.Warning("RemoveFromRemovedGames called with null or empty executablePath");
                    return;
                }

                if (_currentSettings.RemovedGamePaths.Contains(executablePath))
                {
                    _currentSettings.RemovedGamePaths.Remove(executablePath);
                    App.LogService?.Info($"Removed game from removed list: {executablePath}");
                }
                else
                {
                    App.LogService?.LogDebug($"Game not found in removed list: {executablePath}");
                }
            }, "RemoveFromRemovedGames");
        }

        // Set a game as the default
        public void SetDefaultGame(string gameId)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"Setting default game to: {gameId ?? "None"}");
                _currentSettings.DefaultGameId = gameId;

                // If gameId is null, clear default
                if (gameId == null)
                {
                    foreach (var game in _currentSettings.Games)
                    {
                        game.IsDefault = false;
                    }
                    App.LogService?.LogDebug("Cleared default game setting for all games");
                    return;
                }

                // Update the IsDefault flag on all games
                foreach (var game in _currentSettings.Games)
                {
                    bool wasDefault = game.IsDefault;
                    game.IsDefault = game.Id == gameId;

                    if (wasDefault != game.IsDefault)
                    {
                        App.LogService?.LogDebug($"Updated default status for game {game.Name}: {game.IsDefault}");
                    }
                }
            }, "SetDefaultGame");
        }

        // Set the last selected game
        public void SetLastSelectedGame(string gameId)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Setting last selected game to: {gameId ?? "None"}");
                _currentSettings.LastGameId = gameId;
            }, "SetLastSelectedGame");
        }

        // Get the default or last used game ID
        public string GetPreferredGameId()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // Prefer default game if set
                if (!string.IsNullOrEmpty(_currentSettings.DefaultGameId))
                {
                    App.LogService?.Trace($"Using default game ID: {_currentSettings.DefaultGameId}");
                    return _currentSettings.DefaultGameId;
                }

                // Fall back to last selected game
                App.LogService?.Trace($"Using last selected game ID: {_currentSettings.LastGameId ?? "None"}");
                return _currentSettings.LastGameId;
            }, "GetPreferredGameId", false, null);
        }

        // Get the list of removed game paths
        public List<string> GetRemovedGamePaths()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                var paths = _currentSettings.RemovedGamePaths ?? new List<string>();
                App.LogService?.Trace($"Retrieved {paths.Count} removed game paths");
                return paths;
            }, "GetRemovedGamePaths", false, new List<string>());
        }

        // Save applied mods for a specific game
        public async Task SaveAppliedModsAsync(string gameId, List<AppliedModSetting> appliedMods)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveAppliedModsAsync called with null or empty gameId");
                    return;
                }

                if (appliedMods == null)
                {
                    App.LogService?.Warning($"SaveAppliedModsAsync called with null appliedMods for game {gameId}");
                    appliedMods = new List<AppliedModSetting>();
                }

                App.LogService?.Info($"Saving {appliedMods.Count} applied mods for game {gameId}");

                _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
                _currentSettings.AppliedMods[gameId] = appliedMods;

                // Save settings asynchronously
                await SaveSettingsAsync();
                App.LogService?.LogDebug("Applied mods saved successfully");
            }, "SaveAppliedModsAsync");
        }

        // Non-async wrapper for backwards compatibility
        public void SaveAppliedMods(string gameId, List<AppliedModSetting> appliedMods)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveAppliedMods called with null or empty gameId");
                    return;
                }

                if (appliedMods == null)
                {
                    App.LogService?.Warning($"SaveAppliedMods called with null appliedMods for game {gameId}");
                    appliedMods = new List<AppliedModSetting>();
                }

                App.LogService?.Info($"Saving {appliedMods.Count} applied mods for game {gameId}");

                _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
                _currentSettings.AppliedMods[gameId] = appliedMods;

                // Use Task.Run to prevent blocking
                Task.Run(() => SaveSettingsAsync()).ConfigureAwait(false);
                App.LogService?.LogDebug("Initiated background save of applied mods");
            }, "SaveAppliedMods");
        }

        // Save the last applied mods state
        public async Task SaveLastAppliedModsStateAsync(string gameId, List<AppliedModSetting> appliedMods)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveLastAppliedModsStateAsync called with null or empty gameId");
                    return;
                }

                if (appliedMods == null)
                {
                    App.LogService?.Warning($"SaveLastAppliedModsStateAsync called with null appliedMods for game {gameId}");
                    appliedMods = new List<AppliedModSetting>();
                }

                App.LogService?.Info($"Saving last applied state of {appliedMods.Count} mods for game {gameId}");

                _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
                _currentSettings.LastAppliedMods[gameId] = appliedMods;

                // Save settings asynchronously
                await SaveSettingsAsync();
                App.LogService?.LogDebug("Last applied mods state saved successfully");
            }, "SaveLastAppliedModsStateAsync");
        }

        // Non-async wrapper
        public void SaveLastAppliedModsState(string gameId, List<AppliedModSetting> appliedMods)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveLastAppliedModsState called with null or empty gameId");
                    return;
                }

                if (appliedMods == null)
                {
                    App.LogService?.Warning($"SaveLastAppliedModsState called with null appliedMods for game {gameId}");
                    appliedMods = new List<AppliedModSetting>();
                }

                App.LogService?.Info($"Saving last applied state of {appliedMods.Count} mods for game {gameId}");

                _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();
                _currentSettings.LastAppliedMods[gameId] = appliedMods;

                // DON'T use Task.Run - this creates a fire-and-forget operation that's not tracked
                // Instead, use a synchronous save to ensure it completes
                try
                {
                    var startTime = DateTime.Now;
                    App.LogService?.LogDebug("Performing synchronous save of last applied mods state");

                    // Create a backup before saving
                    BackupSettingsFile();

                    string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_configFilePath, json);

                    var elapsed = DateTime.Now - startTime;
                    App.LogService?.LogDebug($"Synchronous save completed in {elapsed.TotalMilliseconds:F1}ms");

                    App.LogService?.Info("Last applied mods state saved successfully");
                }
                catch (Exception ex)
                {
                    App.LogService?.Error($"Error saving settings: {ex.Message}");
                    App.LogService?.LogDebug($"Save error details: {ex}");
                    throw; // Rethrow for the ErrorHandler to catch
                }
            }, "SaveLastAppliedModsState");
        }

        // Get applied mods for a specific game
        public List<AppliedModSetting> GetAppliedMods(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("GetAppliedMods called with null or empty gameId");
                    return new List<AppliedModSetting>();
                }

                _currentSettings.AppliedMods = _currentSettings.AppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();

                if (_currentSettings.AppliedMods.TryGetValue(gameId, out var mods))
                {
                    App.LogService?.Trace($"Retrieved {mods.Count} applied mods for game {gameId}");
                    return mods;
                }

                App.LogService?.Trace($"No applied mods found for game {gameId}");
                return new List<AppliedModSetting>();
            }, "GetAppliedMods", false, new List<AppliedModSetting>());
        }

        // Get the last applied mods state
        public List<AppliedModSetting> GetLastAppliedModsState(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("GetLastAppliedModsState called with null or empty gameId");
                    return null;
                }

                _currentSettings.LastAppliedMods = _currentSettings.LastAppliedMods ?? new Dictionary<string, List<AppliedModSetting>>();

                if (_currentSettings.LastAppliedMods.TryGetValue(gameId, out var mods))
                {
                    App.LogService?.Trace($"Retrieved {mods.Count} last applied mods for game {gameId}");
                    return mods;
                }

                App.LogService?.Trace($"No last applied mods found for game {gameId}");
                return null;
            }, "GetLastAppliedModsState", false, null);
        }

        // Flag to track if mods have been changed
        private bool _modsChanged = false;

        // Mark mods as changed
        public void MarkModsChanged()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                _modsChanged = true;
                App.LogService?.LogDebug("Mods marked as changed");
            }, "MarkModsChanged");
        }

        // Reset the mods changed flag
        public void ResetModsChangedFlag()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                _modsChanged = false;
                App.LogService?.LogDebug("Mods changed flag reset");
            }, "ResetModsChangedFlag");
        }

        // Check if mods have changed since last application
        public bool HaveModsChanged(string gameId, List<AppliedModSetting> currentActiveMods)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("HaveModsChanged called with null or empty gameId");
                    return true; // Consider changed if we don't know the game
                }

                if (currentActiveMods == null)
                {
                    App.LogService?.LogDebug("HaveModsChanged called with null currentActiveMods");
                    return true; // Consider changed if no mods provided
                }

                // Debug which condition is triggering
                if (_modsChanged)
                {
                    App.LogService?.LogDebug("Mods have changed because flag is explicitly set");
                    return true;
                }

                var lastAppliedState = GetLastAppliedModsState(gameId);

                // If no last state exists at all, then mods have changed
                if (lastAppliedState == null)
                {
                    App.LogService?.LogDebug("Mods have changed because last state is null");
                    return true;
                }

                // If both current and last state have 0 mods, nothing changed
                if (lastAppliedState.Count == 0 && currentActiveMods.Count == 0)
                {
                    App.LogService?.LogDebug("No changes: both current and last state have 0 mods");
                    return false;
                }

                // If counts differ, something changed
                if (lastAppliedState.Count != currentActiveMods.Count)
                {
                    App.LogService?.LogDebug($"Mods have changed: count mismatch (current: {currentActiveMods.Count}, last: {lastAppliedState.Count})");
                    return true;
                }

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
                        App.LogService?.LogDebug($"Mods have changed: detected difference at index {i}");
                        App.LogService?.Trace($"Current mod: Path={currentMod.ModFolderPath}, IsArchive={currentMod.IsFromArchive}");
                        App.LogService?.Trace($"Last mod: Path={lastMod.ModFolderPath}, IsArchive={lastMod.IsFromArchive}");
                        return true;
                    }
                }

                // No differences found
                App.LogService?.LogDebug("No mod changes detected");
                return false;
            }, "HaveModsChanged", false, true);
        }

        public async Task SaveProfilesAsync(string gameId, List<ModProfile> profiles)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var startTime = DateTime.Now;

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveProfilesAsync called with null or empty gameId");
                    return;
                }

                if (profiles == null)
                {
                    App.LogService?.Warning($"SaveProfilesAsync called with null profiles for game {gameId}");
                    return;
                }

                App.LogService?.Info($"Saving {profiles.Count} profiles for game {gameId}");

                // Make sure settings object exists
                if (_currentSettings == null)
                {
                    App.LogService?.LogDebug("Settings object is null, loading settings first");
                    await LoadSettingsAsync();
                }

                // Ensure the game profiles dictionary exists
                if (_currentSettings.GameProfiles == null)
                {
                    App.LogService?.LogDebug("Initializing missing GameProfiles dictionary");
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                }

                // Create a backup of the profiles before saving
                BackupSettingsFile();

                // Filter out any null profiles
                var originalCount = profiles.Count;
                var validProfiles = profiles.Where(p => p != null).ToList();

                if (validProfiles.Count < originalCount)
                {
                    App.LogService?.Warning($"Filtered out {originalCount - validProfiles.Count} null profiles");
                }

                // Save the profiles
                _currentSettings.GameProfiles[gameId] = validProfiles;

                // Save settings to disk
                await SaveSettingsAsync();

                var elapsed = DateTime.Now - startTime;
                App.LogService?.LogDebug($"SaveProfilesAsync completed in {elapsed.TotalMilliseconds:F1}ms");
                App.LogService?.Info($"Successfully saved {validProfiles.Count} profiles for game {gameId}");
            }, "SaveProfilesAsync");
        }

        public async Task SaveActiveProfileIdAsync(string gameId, string profileId)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("SaveActiveProfileIdAsync called with null or empty gameId");
                    return;
                }

                if (string.IsNullOrEmpty(profileId))
                {
                    App.LogService?.Warning("SaveActiveProfileIdAsync called with null or empty profileId");
                    return;
                }

                App.LogService?.Info($"Saving active profile ID {profileId} for game {gameId}");

                // Make sure settings object exists
                if (_currentSettings == null)
                {
                    App.LogService?.LogDebug("Settings object is null, loading settings first");
                    await LoadSettingsAsync();
                }

                // Ensure the active profiles dictionary exists
                if (_currentSettings.ActiveProfileIds == null)
                {
                    App.LogService?.LogDebug("Initializing missing ActiveProfileIds dictionary");
                    _currentSettings.ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                // Save the active profile ID
                _currentSettings.ActiveProfileIds[gameId] = profileId;

                // Save settings to disk
                await SaveSettingsAsync();

                App.LogService?.Info($"Active profile ID saved successfully");
            }, "SaveActiveProfileIdAsync");
        }

        public async Task<List<string>> GetGameIdsWithProfilesAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Make sure settings object exists
                if (_currentSettings == null)
                {
                    App.LogService?.LogDebug("Settings object is null, loading settings first");
                    await LoadSettingsAsync();
                }

                // Return empty list if no profiles exist
                if (_currentSettings.GameProfiles == null)
                {
                    App.LogService?.LogDebug("GameProfiles dictionary is null, returning empty list");
                    return new List<string>();
                }

                // Return all game IDs with profiles
                var gameIds = new List<string>(_currentSettings.GameProfiles.Keys);
                App.LogService?.LogDebug($"Retrieved {gameIds.Count} game IDs with profiles");

                return gameIds;
            }, "GetGameIdsWithProfilesAsync", true, new List<string>());
        }

        public async Task<List<ModProfile>> LoadProfilesAsync(string gameId)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("LoadProfilesAsync called with null or empty gameId");
                    return new List<ModProfile>();
                }

                App.LogService?.Info($"Loading profiles for game {gameId}");

                // Make sure settings object exists
                if (_currentSettings == null)
                {
                    App.LogService?.LogDebug("Settings object is null, loading settings first");
                    await LoadSettingsAsync();
                }

                // Initialize if needed
                if (_currentSettings.GameProfiles == null)
                {
                    App.LogService?.LogDebug("Initializing missing GameProfiles dictionary");
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                }

                // Log all available game IDs for debugging
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService.LogDebug("Available game IDs in settings:");
                    foreach (var id in _currentSettings.GameProfiles.Keys)
                    {
                        App.LogService.LogDebug($"  - {id}");
                    }
                }

                // Find profiles for the game
                List<ModProfile> profiles = null;

                if (_currentSettings.GameProfiles.TryGetValue(gameId, out profiles))
                {
                    App.LogService?.Info($"Found {profiles.Count} profiles for game {gameId}");
                    return profiles;
                }

                // Enhanced fuzzy matching - try just the game name
                string gameName = gameId.Split('_')[0];
                App.LogService?.LogDebug($"Exact match not found, trying fuzzy match with game name: {gameName}");

                // Try to find any key that starts with the game name
                var matchingKey = _currentSettings.GameProfiles.Keys
                    .FirstOrDefault(k => k.StartsWith(gameName, StringComparison.OrdinalIgnoreCase));

                if (matchingKey != null)
                {
                    profiles = _currentSettings.GameProfiles[matchingKey];
                    App.LogService?.Info($"Found {profiles.Count} profiles using fuzzy match: {matchingKey}");

                    // Store with new ID to avoid future mismatches
                    _currentSettings.GameProfiles[gameId] = profiles;
                    await SaveSettingsAsync();

                    return profiles;
                }

                // If no profiles found at all, return empty list
                App.LogService?.Info($"No profiles found for game {gameId}, returning empty list");
                return new List<ModProfile>();
            }, "LoadProfilesAsync", true, new List<ModProfile>());
        }

        public async Task<string> LoadActiveProfileIdAsync(string gameId)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("LoadActiveProfileIdAsync called with null or empty gameId");
                    return null;
                }

                // Make sure settings object exists
                if (_currentSettings == null)
                {
                    App.LogService?.LogDebug("Settings object is null, loading settings first");
                    await LoadSettingsAsync();
                }

                // Return null if no active profile ID exists
                if (_currentSettings.ActiveProfileIds == null)
                {
                    App.LogService?.LogDebug("ActiveProfileIds dictionary is null");
                    return null;
                }

                if (!_currentSettings.ActiveProfileIds.ContainsKey(gameId))
                {
                    App.LogService?.LogDebug($"No active profile ID found for game {gameId}");
                    return null;
                }

                // Return the active profile ID
                var profileId = _currentSettings.ActiveProfileIds[gameId];
                App.LogService?.LogDebug($"Retrieved active profile ID for game {gameId}: {profileId}");
                return profileId;
            }, "LoadActiveProfileIdAsync", true, null);
        }

        public List<ModProfile> GetProfiles(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("GetProfiles called with null or empty gameId");
                    return new List<ModProfile>();
                }

                App.LogService?.LogDebug($"GetProfiles: Looking for profiles with game ID: {gameId}");

                // Ensure dictionaries are initialized
                if (_currentSettings.GameProfiles == null)
                {
                    App.LogService?.LogDebug("Initializing missing GameProfiles dictionary");
                    _currentSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                }

                // Log all available game IDs for debugging
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService.LogDebug("Available profile game IDs:");
                    foreach (var key in _currentSettings.GameProfiles.Keys)
                    {
                        App.LogService.LogDebug($"  - {key}");
                    }
                }

                // Check if there are profiles for this exact game ID
                if (_currentSettings.GameProfiles.TryGetValue(gameId, out var exactProfiles))
                {
                    App.LogService?.Info($"Found {exactProfiles.Count} profiles with exact ID match");
                    return exactProfiles;
                }

                // If not found with the exact ID, try to match by game name portion
                string gameName = gameId.Split('_')[0]; // Extract name part before underscore
                App.LogService?.LogDebug($"Trying partial match with game name: {gameName}");

                foreach (var entry in _currentSettings.GameProfiles)
                {
                    // Check if the key starts with the game name
                    if (entry.Key.StartsWith(gameName + "_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.Key, gameName, StringComparison.OrdinalIgnoreCase))
                    {
                        App.LogService?.Info($"Found {entry.Value.Count} profiles using partial name match: {entry.Key}");

                        // Save with new ID to prevent future mismatches
                        _currentSettings.GameProfiles[gameId] = entry.Value;
                        Task.Run(() => SaveSettingsAsync());

                        return entry.Value;
                    }
                }

                // No profiles found - create a default one
                App.LogService?.Info($"No profiles found for game {gameId}, creating default profile");
                var defaultProfile = new ModProfile
                {
                    Name = "Default Profile",
                    Id = Guid.NewGuid().ToString(),
                    LastModified = DateTime.Now
                };

                var newList = new List<ModProfile> { defaultProfile };
                _currentSettings.GameProfiles[gameId] = newList;

                // Save in background to avoid blocking
                Task.Run(() => SaveSettingsAsync());

                return newList;
            }, "GetProfiles", true, new List<ModProfile> { new ModProfile() { Name = "Default" } });
        }

        private void BackupSettingsFile()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (!File.Exists(_configFilePath))
                {
                    App.LogService?.LogDebug("Settings file does not exist, skipping backup");
                    return;
                }

                string backupPath = _configFilePath + ".bak";
                App.LogService?.LogDebug($"Creating backup of settings file at {backupPath}");

                File.Copy(_configFilePath, backupPath, true);
                App.LogService?.Info($"Settings file backed up successfully");
            }, "BackupSettingsFile");
        }

        private async Task<AppSettings> TryLoadFromBackupAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                string backupPath = _configFilePath + ".bak";

                if (!File.Exists(backupPath))
                {
                    App.LogService?.Warning("Backup settings file not found");
                    return _currentSettings;
                }

                App.LogService?.Info("Attempting to load settings from backup file");

                string json = await File.ReadAllTextAsync(backupPath);
                var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                // Initialize required collections
                if (loadedSettings.RemovedGamePaths == null)
                {
                    App.LogService?.LogDebug("Initializing missing RemovedGamePaths collection in backup");
                    loadedSettings.RemovedGamePaths = new List<string>();
                }

                if (loadedSettings.AppliedMods == null)
                {
                    App.LogService?.LogDebug("Initializing missing AppliedMods dictionary in backup");
                    loadedSettings.AppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                }

                if (loadedSettings.LastAppliedMods == null)
                {
                    App.LogService?.LogDebug("Initializing missing LastAppliedMods dictionary in backup");
                    loadedSettings.LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>();
                }

                if (loadedSettings.GameProfiles == null)
                {
                    App.LogService?.LogDebug("Initializing missing GameProfiles dictionary in backup");
                    loadedSettings.GameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
                }

                if (loadedSettings.ActiveProfileIds == null)
                {
                    App.LogService?.LogDebug("Initializing missing ActiveProfileIds dictionary in backup");
                    loadedSettings.ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                _currentSettings = loadedSettings;
                App.LogService?.Info("Successfully loaded settings from backup file");

                // Save to main file to repair it
                await SaveSettingsAsync();

                return _currentSettings;
            }, "TryLoadFromBackupAsync", true, _currentSettings);
        }

        // Get auto-detect games at startup setting
        public bool GetAutoDetectGamesAtStartup()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool result = _currentSettings.AutoDetectGamesAtStartup;
                App.LogService?.Trace($"GetAutoDetectGamesAtStartup: {result}");
                return result;
            }, "GetAutoDetectGamesAtStartup", false, true); // Default to true if error
        }

        // Set auto-detect games at startup setting
        public void SetAutoDetectGamesAtStartup(bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Setting AutoDetectGamesAtStartup to {value}");
                _currentSettings.AutoDetectGamesAtStartup = value;
            }, "SetAutoDetectGamesAtStartup");
        }

        // Get auto-check for updates at startup setting
        public bool GetAutoCheckForUpdatesAtStartup()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool result = _currentSettings.AutoCheckForUpdatesAtStartup;
                App.LogService?.Trace($"GetAutoCheckForUpdatesAtStartup: {result}");
                return result;
            }, "GetAutoCheckForUpdatesAtStartup", false, true); // Default to true if error
        }

        // Set auto-check for updates at startup setting
        public void SetAutoCheckForUpdatesAtStartup(bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Setting AutoCheckForUpdatesAtStartup to {value}");
                _currentSettings.AutoCheckForUpdatesAtStartup = value;
            }, "SetAutoCheckForUpdatesAtStartup");
        }

        // Get detailed logging setting
        public bool GetEnableDetailedLogging()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool result = _currentSettings.EnableDetailedLogging;
                App.LogService?.Trace($"GetEnableDetailedLogging: {result}");
                return result;
            }, "GetEnableDetailedLogging", false, false); // Default to false if error
        }

        // Set detailed logging setting
        public void SetEnableDetailedLogging(bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"Setting EnableDetailedLogging to {value}");
                _currentSettings.EnableDetailedLogging = value;

                // Update the log service as well if available
                if (App.LogService != null)
                {
                    App.LogService.UpdateDetailedLogging(value);
                }
            }, "SetEnableDetailedLogging");
        }

        // Get low usage mode setting
        public bool GetLowUsageMode()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool result = _currentSettings.LowUsageMode;
                App.LogService?.Trace($"GetLowUsageMode: {result}");
                return result;
            }, "GetLowUsageMode", false, false); // Default to false if error
        }

        // Set low usage mode setting
        public void SetLowUsageMode(bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Setting LowUsageMode to {value}");
                _currentSettings.LowUsageMode = value;
            }, "SetLowUsageMode");
        }

        // Get remember last selected game setting
        public bool GetRememberLastSelectedGame()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                bool result = _currentSettings.RememberLastSelectedGame;
                App.LogService?.Trace($"GetRememberLastSelectedGame: {result}");
                return result;
            }, "GetRememberLastSelectedGame", false, false); // Default to false if error
        }

        // Set remember last selected game setting
        public void SetRememberLastSelectedGame(bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Setting RememberLastSelectedGame to {value}");
                _currentSettings.RememberLastSelectedGame = value;
            }, "SetRememberLastSelectedGame");
        }

        // Get launcher action on game launch
        public string GetLauncherActionOnGameLaunch()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                string result = _currentSettings.LauncherActionOnGameLaunch ?? "Minimize";
                App.LogService?.Trace($"GetLauncherActionOnGameLaunch: {result}");
                return result;
            }, "GetLauncherActionOnGameLaunch", false, "Minimize"); // Default to Minimize if error
        }

        // Set launcher action on game launch
        public void SetLauncherActionOnGameLaunch(string value)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                // Validate the value
                if (string.IsNullOrEmpty(value) ||
                    (value != "None" && value != "Minimize" && value != "Close"))
                {
                    App.LogService?.Warning($"Invalid LauncherActionOnGameLaunch value: {value}, defaulting to 'Minimize'");
                    value = "Minimize";
                }

                App.LogService?.LogDebug($"Setting LauncherActionOnGameLaunch to {value}");
                _currentSettings.LauncherActionOnGameLaunch = value;
            }, "SetLauncherActionOnGameLaunch");
        }

        // Get custom game scan paths
        public List<string> GetCustomGameScanPaths()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                var paths = _currentSettings.CustomGameScanPaths ?? new List<string>();
                App.LogService?.Trace($"GetCustomGameScanPaths: Retrieved {paths.Count} paths");
                return paths;
            }, "GetCustomGameScanPaths", false, new List<string>());
        }

        // Add custom game scan path
        public void AddCustomGameScanPath(string path)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    App.LogService?.Warning("AddCustomGameScanPath called with null or empty path");
                    return;
                }

                _currentSettings.CustomGameScanPaths = _currentSettings.CustomGameScanPaths ?? new List<string>();

                // Don't add duplicates
                if (!_currentSettings.CustomGameScanPaths.Contains(path))
                {
                    _currentSettings.CustomGameScanPaths.Add(path);
                    App.LogService?.Info($"Added custom game scan path: {path}");
                }
                else
                {
                    App.LogService?.LogDebug($"Custom game scan path already exists: {path}");
                }
            }, "AddCustomGameScanPath");
        }

        // Remove custom game scan path
        public void RemoveCustomGameScanPath(string path)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    App.LogService?.Warning("RemoveCustomGameScanPath called with null or empty path");
                    return;
                }

                if (_currentSettings.CustomGameScanPaths == null)
                {
                    App.LogService?.LogDebug("CustomGameScanPaths is null, nothing to remove");
                    return;
                }

                if (_currentSettings.CustomGameScanPaths.Contains(path))
                {
                    _currentSettings.CustomGameScanPaths.Remove(path);
                    App.LogService?.Info($"Removed custom game scan path: {path}");
                }
                else
                {
                    App.LogService?.LogDebug($"Custom game scan path not found: {path}");
                }
            }, "RemoveCustomGameScanPath");
        }

        // Clear all custom game scan paths
        public void ClearCustomGameScanPaths()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Clearing all custom game scan paths");
                _currentSettings.CustomGameScanPaths = new List<string>();
            }, "ClearCustomGameScanPaths");
        }

        // Reset to default settings
        public async Task ResetToDefaultSettingsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Resetting to default settings");
                var startTime = DateTime.Now;

                // Save existing Games and GameProfiles data
                var existingGames = _currentSettings.Games;
                var existingProfiles = _currentSettings.GameProfiles;
                var existingRemovedPaths = _currentSettings.RemovedGamePaths;

                // Create a backup before resetting
                BackupSettingsFile();

                // Create a new settings object with defaults
                _currentSettings = new AppSettings
                {
                    Games = existingGames, // Keep game data
                    GameProfiles = existingProfiles, // Keep profiles data
                    RemovedGamePaths = existingRemovedPaths, // Keep removed paths
                    AppliedMods = new Dictionary<string, List<AppliedModSetting>>(),
                    LastAppliedMods = new Dictionary<string, List<AppliedModSetting>>(),
                    ActiveProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    AutoDetectGamesAtStartup = true,
                    AutoCheckForUpdatesAtStartup = true,
                    EnableDetailedLogging = false,
                    LowUsageMode = false,
                    RememberLastSelectedGame = false, // Changed default to match new requirement
                    LauncherActionOnGameLaunch = "Minimize",
                    CustomGameScanPaths = new List<string>()
                };

                // Save the reset settings
                await SaveSettingsAsync();

                var elapsed = DateTime.Now - startTime;
                App.LogService?.LogDebug($"ResetToDefaultSettingsAsync completed in {elapsed.TotalMilliseconds:F1}ms");
                App.LogService?.Info("Settings reset to defaults successfully");
            }, "ResetToDefaultSettingsAsync");
        }

        // Clear cache directories
        public void ClearCache()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Clearing application cache");
                var startTime = DateTime.Now;
                int deletedFiles = 0;
                int errors = 0;

                try
                {
                    string appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AMO_Launcher");

                    // Clear icon cache
                    string iconCachePath = Path.Combine(appDataPath, "IconCache");
                    if (Directory.Exists(iconCachePath))
                    {
                        App.LogService?.LogDebug($"Clearing icon cache at {iconCachePath}");

                        foreach (var file in Directory.GetFiles(iconCachePath))
                        {
                            try
                            {
                                File.Delete(file);
                                deletedFiles++;
                                App.LogService?.Trace($"Deleted cache file: {Path.GetFileName(file)}");
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                App.LogService?.Warning($"Error deleting cache file {file}: {ex.Message}");
                            }
                        }
                    }

                    // Clear update files
                    string updatePath = Path.Combine(appDataPath, "Updates");
                    if (Directory.Exists(updatePath))
                    {
                        App.LogService?.LogDebug($"Clearing update cache at {updatePath}");

                        try
                        {
                            // Count files before deletion for logging
                            int updateFiles = Directory.GetFiles(updatePath, "*", SearchOption.AllDirectories).Length;

                            Directory.Delete(updatePath, true);
                            Directory.CreateDirectory(updatePath);

                            deletedFiles += updateFiles;
                            App.LogService?.LogDebug($"Deleted {updateFiles} update cache files");
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            App.LogService?.Warning($"Error clearing update cache: {ex.Message}");
                            App.LogService?.LogDebug($"Update cache error details: {ex}");
                        }
                    }

                    // Reset icon cache service
                    App.IconCacheService.ClearCache();
                    App.LogService?.LogDebug("Icon cache service reset");

                    var elapsed = DateTime.Now - startTime;
                    App.LogService?.LogDebug($"ClearCache completed in {elapsed.TotalMilliseconds:F1}ms");
                    App.LogService?.Info($"Cache cleared successfully: {deletedFiles} files deleted, {errors} errors");
                }
                catch (Exception ex)
                {
                    App.LogService?.Error($"Failed to clear cache: {ex.Message}");
                    App.LogService?.LogDebug($"Cache clear error details: {ex}");
                    throw; // Rethrow for ErrorHandler
                }
            }, "ClearCache");
        }

        // Get the default game ID
        public string GetDefaultGameId()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                string id = _currentSettings.DefaultGameId;
                App.LogService?.Trace($"GetDefaultGameId: {id ?? "None"}");
                return id;
            }, "GetDefaultGameId", false, null);
        }

        // Get the last selected game ID
        public string GetLastSelectedGameId()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                string id = _currentSettings.LastGameId;
                App.LogService?.Trace($"GetLastSelectedGameId: {id ?? "None"}");
                return id;
            }, "GetLastSelectedGameId", false, null);
        }

        // Helper method to check if settings have pending changes
        public bool HasPendingChanges()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // This is a simple implementation that always returns false
                // A real implementation would track changes to know if there are pending saves
                return false;
            }, "HasPendingChanges", false, false);
        }

        // Synchronous save method for shutdown scenarios
        public void SaveSettingsSync()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Performing synchronous settings save");

                string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Create a backup before saving
                BackupSettingsFile();

                // Write directly to file
                File.WriteAllText(_configFilePath, json);

                App.LogService?.Info("Settings saved synchronously");
            }, "SaveSettingsSync");
        }
    }

    // Classes for serialization
    public class AppSettings
    {
        public List<GameSetting> Games { get; set; }
        public List<string> RemovedGamePaths { get; set; } = new List<string>();
        public string DefaultGameId { get; set; }
        public string LastGameId { get; set; }
        public Dictionary<string, List<AppliedModSetting>> AppliedMods { get; set; } = new Dictionary<string, List<AppliedModSetting>>();
        public Dictionary<string, List<AppliedModSetting>> LastAppliedMods { get; set; } = new Dictionary<string, List<AppliedModSetting>>();
        public Dictionary<string, List<ModProfile>> GameProfiles { get; set; } = new Dictionary<string, List<ModProfile>>();
        public Dictionary<string, string> ActiveProfileIds { get; set; } = new Dictionary<string, string>();
        public bool AutoDetectGamesAtStartup { get; set; } = true;
        public bool AutoCheckForUpdatesAtStartup { get; set; } = true;
        public bool EnableDetailedLogging { get; set; } = false;
        public bool LowUsageMode { get; set; } = false;
        public bool RememberLastSelectedGame { get; set; } = false; // Changed from true to false
        public List<string> CustomGameScanPaths { get; set; } = new List<string>();
        public string LauncherActionOnGameLaunch { get; set; } = "Minimize"; // Options: None, Minimize, Close
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
using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace AMO_Launcher.Services
{
    public class ProfileService
    {
        private readonly ConfigurationService _configService;
        private readonly ProfileStorageService _storageService;

        // Dictionary to store profiles
        private Dictionary<string, List<ModProfile>> _gameProfiles = new Dictionary<string, List<ModProfile>>();

        // Dictionary to track active profiles
        private Dictionary<string, string> _activeProfileIds = new Dictionary<string, string>();

        // Constructor - keep initialization super minimal
        public ProfileService(ConfigurationService configService)
        {
            _configService = configService;
            _storageService = new ProfileStorageService();

            // Initialize with empty dictionaries (case-insensitive)
            _gameProfiles = new Dictionary<string, List<ModProfile>>(StringComparer.OrdinalIgnoreCase);
            _activeProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Get profiles for a game
        public List<ModProfile> GetProfilesForGame(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // Always normalize the game ID first
                gameId = NormalizeGameId(gameId);

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("GetProfilesForGame: gameId is null or empty, returning empty list");
                    return new List<ModProfile>();
                }

                // Log the normalized game ID we're using
                App.LogService?.LogDebug($"GetProfilesForGame: Looking for profiles for normalized game ID '{gameId}'");

                // First check if we already have profiles in memory
                if (_gameProfiles.ContainsKey(gameId))
                {
                    var profiles = _gameProfiles[gameId];
                    App.LogService?.LogDebug($"Found {profiles.Count} profiles in memory for game '{gameId}'");
                    return profiles;
                }

                // If not in memory, check storage before creating default
                try
                {
                    // Create a synchronous wrapper around the async method
                    var task = Task.Run(() => _storageService.GetProfilesForGameAsync(gameId));
                    task.Wait();
                    var profilesFromStorage = task.Result;

                    // If profiles were found in storage, use them
                    if (profilesFromStorage != null && profilesFromStorage.Count > 0)
                    {
                        App.LogService?.LogDebug($"Found {profilesFromStorage.Count} profiles in storage for game '{gameId}'");
                        _gameProfiles[gameId] = profilesFromStorage;

                        // Also get the active profile ID
                        var activeIdTask = Task.Run(() => _storageService.GetActiveProfileIdAsync(gameId));
                        activeIdTask.Wait();
                        string activeProfileId = activeIdTask.Result;

                        if (!string.IsNullOrEmpty(activeProfileId))
                        {
                            App.LogService?.LogDebug($"Found active profile ID in storage: {activeProfileId} for game '{gameId}'");
                            _activeProfileIds[gameId] = activeProfileId;
                        }
                        else if (profilesFromStorage.Count > 0)
                        {
                            // Default to first profile if no active ID
                            _activeProfileIds[gameId] = profilesFromStorage[0].Id;
                            App.LogService?.LogDebug($"No active profile found, setting first profile as active: {profilesFromStorage[0].Id}");

                            // Save this active profile ID to storage
                            var setActiveTask = Task.Run(() => _storageService.SetActiveProfileAsync(gameId, profilesFromStorage[0].Id));
                            setActiveTask.Wait();
                        }

                        return _gameProfiles[gameId];
                    }
                    else
                    {
                        App.LogService?.LogDebug($"No profiles found in storage for game '{gameId}'");
                    }
                }
                catch (Exception ex)
                {
                    App.LogService?.LogDebug($"Error checking storage for existing profiles: {ex.Message}");
                    // Continue to create default profile
                }

                // No profiles found in memory or storage, create a default profile
                var defaultProfile = new ModProfile();
                defaultProfile.Id = Guid.NewGuid().ToString();

                App.LogService?.Info($"Creating new default profile for game '{gameId}' with ID {defaultProfile.Id}");

                // Initialize the collections if needed
                _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
                _activeProfileIds[gameId] = defaultProfile.Id;

                // Save this new profile to persistent storage
                Task.Run(() =>
                {
                    _storageService.SaveProfileAsync(gameId, defaultProfile);
                    _storageService.SetActiveProfileAsync(gameId, defaultProfile.Id);
                    SaveProfilesAsync();
                }).ConfigureAwait(false);

                return _gameProfiles[gameId];
            }, "Get profiles for game", true, new List<ModProfile>());
        }

        // Get active profile
        public ModProfile GetActiveProfile(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // Normalize game ID
                gameId = NormalizeGameId(gameId);

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.LogDebug("GetActiveProfile: gameId is null or empty, returning new profile");
                    return new ModProfile();
                }

                // Make sure the profiles list exists
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    App.LogService?.LogDebug($"GetActiveProfile: No profiles found for game '{gameId}', loading profiles");
                    GetProfilesForGame(gameId); // This creates a default profile if needed
                }

                // Get the active profile id
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(gameId))
                {
                    activeProfileId = _activeProfileIds[gameId];
                    App.LogService?.LogDebug($"GetActiveProfile: Found active profile ID: {activeProfileId}");
                }

                // Find the active profile
                if (!string.IsNullOrEmpty(activeProfileId))
                {
                    foreach (var profile in _gameProfiles[gameId])
                    {
                        if (profile.Id == activeProfileId)
                        {
                            App.LogService?.LogDebug($"GetActiveProfile: Found active profile: {profile.Name}");
                            return profile;
                        }
                    }
                }

                // If no active profile found, use the first one
                if (_gameProfiles[gameId].Count > 0)
                {
                    var firstProfile = _gameProfiles[gameId][0];
                    _activeProfileIds[gameId] = firstProfile.Id;
                    App.LogService?.LogDebug($"GetActiveProfile: Using first profile as active: {firstProfile.Name}");

                    // Save this change
                    Task.Run(() => SaveProfilesAsync()).ConfigureAwait(false);

                    return firstProfile;
                }

                // Shouldn't reach here normally, but create a new profile if needed
                App.LogService?.Warning("GetActiveProfile: No profiles found, creating new default profile");
                var newProfile = new ModProfile();
                _gameProfiles[gameId] = new List<ModProfile> { newProfile };
                _activeProfileIds[gameId] = newProfile.Id;

                return newProfile;
            }, "Get active profile", true, new ModProfile());
        }

        // Set active profile
        public async Task SetActiveProfileAsync(string gameId, string profileId)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogService?.Warning("SetActiveProfileAsync: gameId or profileId is null");
                    return;
                }

                // Always normalize the game ID
                string normalizedGameId = NormalizeGameId(gameId);

                App.LogService?.Info($"SetActiveProfileAsync: Setting profile '{profileId}' as active for game '{normalizedGameId}' (original: '{gameId}')");

                // Update in-memory collection with normalized game ID
                _activeProfileIds[normalizedGameId] = profileId;

                // Save active profile ID to file storage
                await _storageService.SetActiveProfileAsync(normalizedGameId, profileId);

                // Verify by trying to read it back
                string savedId = await _storageService.GetActiveProfileIdAsync(normalizedGameId);
                if (savedId == profileId)
                {
                    App.LogService?.LogDebug($"Successfully verified active profile set to '{profileId}'");
                }
                else
                {
                    App.LogService?.Warning($"Failed to verify active profile ID (got '{savedId}' instead of '{profileId}')");
                }
            }, $"Set active profile for game {gameId}", true);
        }

        // Update active profile mods
        public async Task UpdateActiveProfileModsAsync(string gameId, List<AppliedModSetting> mods)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Always normalize the game ID
                string normalizedGameId = NormalizeGameId(gameId);

                App.LogService?.LogDebug($"UpdateActiveProfileModsAsync: Updating mods for game '{normalizedGameId}' (original: '{gameId}')");

                if (string.IsNullOrEmpty(normalizedGameId))
                {
                    App.LogService?.Error("Game ID is null or empty - cannot update mods");
                    return;
                }

                // Make sure we have the profiles dictionary for this game
                if (!_gameProfiles.ContainsKey(normalizedGameId))
                {
                    App.LogService?.LogDebug($"No profiles found for game '{normalizedGameId}', loading from storage first");

                    // Try to load from storage before creating new profiles
                    var profiles = await _storageService.GetProfilesForGameAsync(normalizedGameId);
                    if (profiles != null && profiles.Count > 0)
                    {
                        _gameProfiles[normalizedGameId] = profiles;
                        App.LogService?.LogDebug($"Loaded {profiles.Count} profiles from storage");
                    }
                    else
                    {
                        // Create a default profile if no profiles were found
                        App.LogService?.LogDebug($"No profiles found in storage, creating default profile");
                        _gameProfiles[normalizedGameId] = new List<ModProfile> { new ModProfile() };
                    }
                }

                // Get the active profile ID - always use normalized game ID
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(normalizedGameId))
                {
                    activeProfileId = _activeProfileIds[normalizedGameId];
                    App.LogService?.LogDebug($"Found active profile ID: {activeProfileId}");
                }
                else
                {
                    // Try to load active profile ID from storage
                    activeProfileId = await _storageService.GetActiveProfileIdAsync(normalizedGameId);

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        _activeProfileIds[normalizedGameId] = activeProfileId;
                        App.LogService?.LogDebug($"Loaded active profile ID from storage: {activeProfileId}");
                    }
                    else
                    {
                        App.LogService?.LogDebug("No active profile ID found for this game");
                    }
                }

                // Find the active profile
                ModProfile activeProfile = null;
                if (!string.IsNullOrEmpty(activeProfileId))
                {
                    activeProfile = _gameProfiles[normalizedGameId].FirstOrDefault(p => p.Id == activeProfileId);
                    if (activeProfile != null)
                    {
                        App.LogService?.LogDebug($"Found active profile: {activeProfile.Name}");
                    }
                    else
                    {
                        App.LogService?.Warning($"Active profile with ID {activeProfileId} not found");
                    }
                }

                // If no active profile found, use the first one
                if (activeProfile == null && _gameProfiles[normalizedGameId].Count > 0)
                {
                    activeProfile = _gameProfiles[normalizedGameId][0];
                    _activeProfileIds[normalizedGameId] = activeProfile.Id;
                    App.LogService?.LogDebug($"Using first profile: {activeProfile.Name}");
                }

                // If we have an active profile, update its mods
                if (activeProfile != null)
                {
                    activeProfile.AppliedMods = new List<AppliedModSetting>(mods);
                    activeProfile.LastModified = DateTime.Now;
                    App.LogService?.Info($"Updated profile '{activeProfile.Name}' with {mods.Count} mods");

                    // Save changes to persistent storage - always use normalized gameId
                    await _storageService.SaveProfileAsync(normalizedGameId, activeProfile);
                    await _storageService.SetActiveProfileAsync(normalizedGameId, activeProfile.Id);

                    // Also save to settings for backward compatibility
                    _configService.SaveAppliedMods(normalizedGameId, mods);
                    App.LogService?.LogDebug("Saved mods to config service for backward compatibility");
                }
                else
                {
                    App.LogService?.Warning("No active profile available to update");
                }
            }, $"Update active profile mods for game {gameId}", true);
        }

        // Create a new profile
        public async Task<ModProfile> CreateProfileAsync(string gameId, string profileName)
        {
            return await ErrorHandler.ExecuteSafeAsync<ModProfile>(async () =>
            {
                // Normalize gameId
                gameId = NormalizeGameId(gameId);

                App.LogService?.Info($"CreateProfileAsync: Creating profile '{profileName}' for game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileName))
                {
                    App.LogService?.Error("CreateProfileAsync: gameId or profileName is null");
                    return null;
                }

                // IMPORTANT: Instead of setting active profile attributes, we just 
                // save a copy of the active profile to storage before creating a new one
                string activeId = null;
                ModProfile activeProfile = null;

                if (_activeProfileIds.ContainsKey(gameId) && _gameProfiles.ContainsKey(gameId))
                {
                    activeId = _activeProfileIds[gameId];
                    activeProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == activeId);
                    App.LogService?.LogDebug($"Found active profile: {activeProfile?.Name} (ID: {activeId})");

                    if (activeProfile != null)
                    {
                        // Save active profile to storage before creating a new one - don't change it
                        await _storageService.SaveProfileAsync(gameId, activeProfile);
                        App.LogService?.LogDebug($"Saved current active profile to storage");
                    }
                }

                // Create a new profile with a unique ID
                var newProfile = new ModProfile
                {
                    Name = profileName,
                    Id = Guid.NewGuid().ToString(),
                    LastModified = DateTime.Now,
                    AppliedMods = new List<AppliedModSetting>() // Initialize with empty list
                };
                App.LogService?.Info($"Created new profile object with ID: {newProfile.Id} and Name: {profileName}");

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                    App.LogService?.LogDebug($"Created new profiles collection for game {gameId}");
                }

                // Get current profiles list for debugging
                var existingProfiles = _gameProfiles[gameId];
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService?.LogDebug($"Current profiles for game {gameId}:");
                    foreach (var p in existingProfiles)
                    {
                        App.LogService?.LogDebug($"  - {p.Name} (ID: {p.Id})");
                    }
                }

                // Ensure the name is unique
                string baseName = profileName;
                int counter = 1;

                while (_gameProfiles[gameId].Any(p => p.Name == newProfile.Name))
                {
                    newProfile.Name = $"{baseName} ({counter++})";
                    App.LogService?.LogDebug($"Renamed profile to ensure uniqueness: {newProfile.Name}");
                }

                // Add the new profile to the collection
                _gameProfiles[gameId].Add(newProfile);
                App.LogService?.LogDebug($"Added new profile to collection, now have {_gameProfiles[gameId].Count} profiles");

                // Save the new profile to file
                await _storageService.SaveProfileAsync(gameId, newProfile);

                // Save changes to disk immediately
                await SaveProfilesAsync();
                App.LogService?.Info("Saved profiles to storage");

                // Verify the profile was added properly
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService?.LogDebug("Profiles after adding new one:");
                    foreach (var p in _gameProfiles[gameId])
                    {
                        App.LogService?.LogDebug($"  - {p.Name} (ID: {p.Id})");
                    }
                }

                return newProfile;
            }, $"Create profile '{profileName}' for game {gameId}", true);
        }

        // Delete a profile
        public async Task<bool> DeleteProfileAsync(string gameId, string profileId)
        {
            return await ErrorHandler.ExecuteSafeAsync<bool>(async () =>
            {
                // Normalize gameId
                gameId = NormalizeGameId(gameId);

                App.LogService?.Info($"DeleteProfileAsync: Deleting profile {profileId} from game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogService?.Error("DeleteProfileAsync: gameId or profileId is null");
                    return false;
                }

                // Check if profiles list exists
                if (!_gameProfiles.TryGetValue(gameId, out var profiles) || profiles == null)
                {
                    App.LogService?.Warning($"DeleteProfileAsync: No profiles found for game {gameId}");
                    return false;
                }

                // Log all profiles before deletion
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService?.LogDebug($"Available profiles before deletion:");
                    foreach (var profile in profiles)
                    {
                        App.LogService?.LogDebug($"  - {profile.Name} (ID: {profile.Id})");
                    }
                }

                // Find the profile
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == profileId);

                if (profileToRemove == null)
                {
                    App.LogService?.Warning($"DeleteProfileAsync: Profile with ID {profileId} not found");
                    return false;
                }

                App.LogService?.LogDebug($"Found profile to delete: {profileToRemove.Name} (ID: {profileId})");

                // Check if it's the active profile
                if (_activeProfileIds.TryGetValue(gameId, out string activeProfileId) &&
                    activeProfileId == profileId)
                {
                    App.LogService?.LogDebug("This is the active profile, need to select another one");

                    // If we're removing the active profile, select another one
                    if (profiles.Count > 1)
                    {
                        // Find a different profile to make active
                        var newActiveProfile = profiles.FirstOrDefault(p => p.Id != profileId);
                        if (newActiveProfile != null)
                        {
                            _activeProfileIds[gameId] = newActiveProfile.Id;
                            await _storageService.SetActiveProfileAsync(gameId, newActiveProfile.Id);
                            App.LogService?.Info($"Set new active profile: {newActiveProfile.Name} (ID: {newActiveProfile.Id})");
                        }
                    }
                    else
                    {
                        App.LogService?.LogDebug("This is the only profile, creating a new default profile");
                        // If this was the only profile, create a new default
                        var defaultProfile = new ModProfile();
                        _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
                        _activeProfileIds[gameId] = defaultProfile.Id;
                        await _storageService.SetActiveProfileAsync(gameId, defaultProfile.Id);
                        await _storageService.SaveProfileAsync(gameId, defaultProfile);

                        App.LogService?.Info($"Created new default profile with ID: {defaultProfile.Id}");
                        return true; // We've handled this special case
                    }
                }

                // Remove the profile from memory
                bool removed = profiles.Remove(profileToRemove);
                App.LogService?.LogDebug($"Profile removed from memory: {removed}");

                // Delete profile file
                bool fileDeleted = await _storageService.DeleteProfileAsync(gameId, profileId);
                App.LogService?.LogDebug($"Profile file deleted: {fileDeleted}");

                // Log profiles after deletion
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService?.LogDebug($"Profiles after deletion:");
                    foreach (var profile in profiles)
                    {
                        App.LogService?.LogDebug($"  - {profile.Name} (ID: {profile.Id})");
                    }
                }

                // Save changes to disk
                await SaveProfilesAsync();
                App.LogService?.LogDebug("Saved profiles after deletion");

                return removed;
            }, $"Delete profile from game {gameId}", true, false);
        }

        // Export profile to JSON
        public async Task<bool> ExportProfileAsync(string gameId, string profileId, string exportPath = null)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info($"Exporting profile {profileId} from game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    App.LogService?.Error("ExportProfileAsync: Invalid parameters");
                    return false;
                }

                // Find the profile
                var profile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (profile == null)
                {
                    App.LogService?.Error($"Profile with ID {profileId} not found");
                    return false;
                }

                // Create export path if not provided
                if (string.IsNullOrEmpty(exportPath))
                {
                    string safeGameId = new string(gameId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    string safeName = new string(profile.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    exportPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AMO Launcher",
                        "Profiles",
                        $"{safeGameId}_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                    );

                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
                    App.LogService?.LogDebug($"Created export path: {exportPath}");
                }

                // Serialize and save
                var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(exportPath, json);
                App.LogService?.Info($"Successfully exported profile '{profile.Name}' to {exportPath}");

                return true;
            }, $"Export profile {profileId} from game {gameId}", true, false);
        }

        // Import profile from JSON
        public async Task<ModProfile> ImportProfileAsync(string gameId, string importPath = null)
        {
            return await ErrorHandler.ExecuteSafeAsync<ModProfile>(async () =>
            {
                App.LogService?.Info($"Importing profile for game {gameId}");

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Error("ImportProfileAsync: gameId is null");
                    return null;
                }

                // If path not provided, a UI would normally prompt for selection
                if (string.IsNullOrEmpty(importPath))
                {
                    App.LogService?.Warning("Import path not provided");
                    return null;
                }

                if (!File.Exists(importPath))
                {
                    App.LogService?.Error($"Import file not found at path: {importPath}");
                    return null;
                }

                var json = await File.ReadAllTextAsync(importPath);
                var importedProfile = JsonSerializer.Deserialize<ModProfile>(json);

                if (importedProfile == null)
                {
                    App.LogService?.Error("Failed to deserialize profile from JSON");
                    return null;
                }

                // Generate a new ID to avoid conflicts
                importedProfile.Id = Guid.NewGuid().ToString();
                importedProfile.LastModified = DateTime.Now;

                App.LogService?.LogDebug($"Imported profile: {importedProfile.Name}, assigned new ID: {importedProfile.Id}");

                // Ensure the name is unique
                string baseName = importedProfile.Name;
                int counter = 1;

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                }

                // Ensure the name is unique
                while (_gameProfiles[gameId].Any(p => p.Name == importedProfile.Name))
                {
                    importedProfile.Name = $"{baseName} (Imported {counter++})";
                    App.LogService?.LogDebug($"Renamed profile to ensure uniqueness: {importedProfile.Name}");
                }

                // Add the profile
                _gameProfiles[gameId].Add(importedProfile);
                App.LogService?.Info($"Added imported profile '{importedProfile.Name}' to game {gameId}");

                // Save to file storage
                await _storageService.SaveProfileAsync(gameId, importedProfile);

                return importedProfile;
            }, $"Import profile for game {gameId}", true);
        }

        // Update profile name
        public Task<bool> UpdateProfileNameAsync(string gameId, string profileId, string newName)
        {
            return ErrorHandler.ExecuteSafeAsync(() =>
            {
                App.LogService?.Info($"Updating name of profile {profileId} to '{newName}'");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    string.IsNullOrEmpty(newName) || !_gameProfiles.ContainsKey(gameId))
                {
                    App.LogService?.Error("UpdateProfileNameAsync: Invalid parameters");
                    return Task.FromResult(false);
                }

                var profile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (profile == null)
                {
                    App.LogService?.Error($"Profile with ID {profileId} not found");
                    return Task.FromResult(false);
                }

                string oldName = profile.Name;
                profile.Name = newName;
                profile.LastModified = DateTime.Now;

                App.LogService?.Info($"Renamed profile from '{oldName}' to '{newName}'");

                // Save updated profile
                _storageService.SaveProfileAsync(gameId, profile).ConfigureAwait(false);

                return Task.FromResult(true);
            }, $"Update profile name for {profileId}", true, false);
        }

        // Create a duplicate of an existing profile
        public async Task<ModProfile> DuplicateProfileAsync(string gameId, string profileId, string newName = null)
        {
            return await ErrorHandler.ExecuteSafeAsync<ModProfile>(async () =>
            {
                App.LogService?.Info($"Duplicating profile {profileId} for game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    App.LogService?.Error("DuplicateProfileAsync: Invalid parameters");
                    return null;
                }

                var sourceProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (sourceProfile == null)
                {
                    App.LogService?.Error($"Source profile with ID {profileId} not found");
                    return null;
                }

                // Create new profile with copy of applied mods
                var duplicateProfile = new ModProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = newName ?? $"{sourceProfile.Name} (Copy)",
                    LastModified = DateTime.Now,
                    AppliedMods = sourceProfile.AppliedMods?.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.ModFolderPath,
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.ArchiveSource,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList() ?? new List<AppliedModSetting>()
                };

                App.LogService?.LogDebug($"Created duplicate profile with ID: {duplicateProfile.Id}, Name: {duplicateProfile.Name}");
                App.LogService?.LogDebug($"Copied {duplicateProfile.AppliedMods?.Count ?? 0} mods to the new profile");

                _gameProfiles[gameId].Add(duplicateProfile);

                // Save to file storage
                await _storageService.SaveProfileAsync(gameId, duplicateProfile);
                App.LogService?.Info($"Successfully duplicated profile '{sourceProfile.Name}' to '{duplicateProfile.Name}'");

                return duplicateProfile;
            }, $"Duplicate profile for game {gameId}", true);
        }

        // Save profiles to persistent storage
        public async Task SaveProfilesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Saving profiles to persistent storage");

                // Save profiles for each game
                foreach (var gameId in _gameProfiles.Keys.ToList())  // Create a copy of keys to avoid modification issues
                {
                    try
                    {
                        // Skip empty game IDs
                        if (string.IsNullOrEmpty(gameId))
                        {
                            App.LogService?.LogDebug("Skipping empty gameId");
                            continue;
                        }

                        // Log detailed info about what we're saving
                        var profiles = _gameProfiles[gameId];

                        // Skip if no profiles to save
                        if (profiles == null || profiles.Count == 0)
                        {
                            App.LogService?.LogDebug($"No profiles to save for game {gameId}");
                            continue;
                        }

                        App.LogService?.LogDebug($"Saving {profiles.Count} profiles for game {gameId}");

                        foreach (var profile in profiles)
                        {
                            // Skip null profiles (shouldn't happen but let's be careful)
                            if (profile == null)
                            {
                                App.LogService?.Warning("Skipping null profile");
                                continue;
                            }

                            App.LogService?.Trace($"  - Profile: {profile.Name} (ID: {profile.Id}), Mods: {profile.AppliedMods?.Count ?? 0}");

                            // Save to file storage
                            await _storageService.SaveProfileAsync(gameId, profile);
                        }

                        // Save to config service for backward compatibility
                        await _configService.SaveProfilesAsync(gameId, profiles);

                        // Save active profile ID
                        if (_activeProfileIds.TryGetValue(gameId, out string activeId))
                        {
                            await _storageService.SetActiveProfileAsync(gameId, activeId);
                            await _configService.SaveActiveProfileIdAsync(gameId, activeId);
                            App.LogService?.LogDebug($"Saved active profile ID {activeId} for game {gameId}");
                        }
                        else
                        {
                            App.LogService?.LogDebug($"No active profile ID for game {gameId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but continue with next game
                        App.LogService?.Error($"Error saving profiles for game {gameId}: {ex.Message}");
                    }
                }

                App.LogService?.Info("Profiles saved successfully");
            }, "Save profiles", false);
        }

        // Load profiles from persistent storage
        public async Task LoadProfilesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Loading profiles from persistent storage");

                // Clear existing data
                _gameProfiles.Clear();
                _activeProfileIds.Clear();

                // Try loading from file storage first
                bool loadedFromFiles = await LoadProfilesFromFilesAsync();

                if (!loadedFromFiles)
                {
                    // If nothing loaded from files, try loading from settings and migrate
                    App.LogService?.Info("No profiles loaded from files, trying settings");
                    await LoadProfilesFromSettingsAsync();

                    // Migrate profiles to file storage
                    await MigrateProfilesToFilesAsync();
                }

                // Always normalize game IDs to ensure consistency
                await NormalizeAllGameIdsAsync();

                App.LogService?.Info($"Loaded profiles for {_gameProfiles.Count} games");
            }, "Load profiles from storage", true);
        }

        // Add this method to normalize all game IDs after loading
        private async Task NormalizeAllGameIdsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.LogDebug("Normalizing all game IDs after loading profiles");

                // Use the enhanced normalization
                bool madeChanges = EnsureNormalizedGameIds();

                if (madeChanges)
                {
                    App.LogService?.Info("Game ID normalization made changes - saving to storage");
                    await SaveProfilesAsync();
                }
            }, "Normalize game IDs", false);
        }

        // Load profiles from files
        private async Task<bool> LoadProfilesFromFilesAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Get all game IDs that have profiles
                var gameIds = await _storageService.GetGameIdsWithProfilesAsync();
                int totalProfilesLoaded = 0;
                bool foundAnyProfiles = false;

                App.LogService?.LogDebug($"Found {gameIds.Count} games with profiles in storage");

                foreach (var gameId in gameIds)
                {
                    // Load profiles for this game
                    var profiles = await _storageService.GetProfilesForGameAsync(gameId);

                    if (profiles != null && profiles.Count > 0)
                    {
                        foundAnyProfiles = true;
                        _gameProfiles[gameId] = profiles;
                        totalProfilesLoaded += profiles.Count;

                        // Load active profile ID
                        string activeId = await _storageService.GetActiveProfileIdAsync(gameId);
                        if (!string.IsNullOrEmpty(activeId))
                        {
                            _activeProfileIds[gameId] = activeId;
                            App.LogService?.LogDebug($"Loaded active profile ID {activeId} for game {gameId}");
                        }
                        else if (profiles.Count > 0)
                        {
                            // Default to first profile if no active ID
                            _activeProfileIds[gameId] = profiles[0].Id;
                            App.LogService?.LogDebug($"No active profile ID found, using first profile as active: {profiles[0].Id}");
                        }

                        App.LogService?.LogDebug($"Loaded {profiles.Count} profiles for game {gameId} from files");
                    }
                }

                // Only return true if we found at least one profile and loaded something
                return foundAnyProfiles && totalProfilesLoaded > 0;
            }, "Load profiles from files", false, false);
        }

        // Load profiles from settings
        private async Task LoadProfilesFromSettingsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Get all game IDs with saved profiles
                var gameIds = await _configService.GetGameIdsWithProfilesAsync();
                App.LogService?.LogDebug($"Found {gameIds.Count} games with profiles in settings");

                foreach (var gameId in gameIds)
                {
                    try
                    {
                        // Skip empty game IDs
                        if (string.IsNullOrEmpty(gameId))
                        {
                            continue;
                        }

                        // Load profiles for this game
                        var profiles = await _configService.LoadProfilesAsync(gameId);

                        // Only add if we got valid profiles
                        if (profiles != null && profiles.Count > 0)
                        {
                            // Filter out any null profiles
                            var validProfiles = profiles.Where(p => p != null).ToList();

                            if (validProfiles.Count > 0)
                            {
                                _gameProfiles[gameId] = validProfiles;

                                // Initialize AppliedMods collection for each profile if needed
                                foreach (var profile in validProfiles)
                                {
                                    if (profile.AppliedMods == null)
                                    {
                                        profile.AppliedMods = new List<AppliedModSetting>();
                                    }
                                }

                                // Load active profile ID
                                string activeId = await _configService.LoadActiveProfileIdAsync(gameId);
                                if (!string.IsNullOrEmpty(activeId))
                                {
                                    _activeProfileIds[gameId] = activeId;
                                    App.LogService?.LogDebug($"Loaded active profile ID {activeId} for game {gameId} from settings");
                                }
                                else if (validProfiles.Count > 0)
                                {
                                    // Default to first profile if no active ID saved
                                    _activeProfileIds[gameId] = validProfiles[0].Id;
                                    App.LogService?.LogDebug($"No active profile ID found in settings, using first profile: {validProfiles[0].Id}");
                                }

                                App.LogService?.LogDebug($"Loaded {validProfiles.Count} profiles for game {gameId} from settings");
                            }
                        }
                        else
                        {
                            App.LogService?.LogDebug($"No profiles found in settings for game {gameId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but continue with next game
                        App.LogService?.Error($"Error loading profiles for game {gameId}: {ex.Message}");
                    }
                }
            }, "Load profiles from settings", false);
        }

        // Migrate profiles from settings to files
        private async Task MigrateProfilesToFilesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (_gameProfiles.Count == 0)
                {
                    App.LogService?.LogDebug("No profiles to migrate");
                    return;
                }

                App.LogService?.Info($"Migrating {_gameProfiles.Count} games' profiles to files");

                // Migrate profiles and active profile IDs
                await _storageService.MigrateFromSettingsAsync(_gameProfiles, _activeProfileIds);

                App.LogService?.Info("Migration complete");
            }, "Migrate profiles to files", false);
        }

        public Task<ModProfile> ImportProfileDirectAsync(string gameId, ModProfile importedProfile)
        {
            return ErrorHandler.ExecuteSafeAsync<ModProfile>(() =>
            {
                App.LogService?.Info($"ImportProfileDirectAsync: Importing profile for game {gameId}");

                if (string.IsNullOrEmpty(gameId) || importedProfile == null)
                {
                    App.LogService?.Error("ImportProfileDirectAsync: gameId is null or importedProfile is null");
                    return Task.FromResult<ModProfile>(null);
                }

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                    App.LogService?.LogDebug($"Created new profiles collection for game {gameId}");
                }

                // Add the profile
                _gameProfiles[gameId].Add(importedProfile);
                App.LogService?.Info($"Added profile {importedProfile.Name} to game {gameId}");

                // Save to file storage
                _storageService.SaveProfileAsync(gameId, importedProfile).ConfigureAwait(false);

                return Task.FromResult(importedProfile);
            }, $"Import profile directly for game {gameId}", true);
        }

        private string NormalizeGameId(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return gameId;

            // Trim any whitespace
            gameId = gameId.Trim();

            // Remove any numeric suffix after underscore
            int underscoreIndex = gameId.IndexOf('_');
            if (underscoreIndex > 0)
            {
                // Take only the part before the underscore
                return gameId.Substring(0, underscoreIndex);
            }

            return gameId;
        }

        // Helper method to ensure consistent game ID normalization
        private bool EnsureNormalizedGameIds()
        {
            bool madeChanges = false;

            // First, create a temporary copy of the keys to avoid modification issues
            var gameIds = _gameProfiles.Keys.ToList();

            foreach (var gameId in gameIds)
            {
                string normalizedId = NormalizeGameId(gameId);

                // Skip if already normalized
                if (normalizedId == gameId)
                    continue;

                App.LogService?.Info($"Converting game ID from '{gameId}' to normalized form '{normalizedId}'");

                // If profiles already exist for normalized ID, merge them
                if (_gameProfiles.ContainsKey(normalizedId))
                {
                    App.LogService?.LogDebug($"Merging {_gameProfiles[gameId].Count} profiles from '{gameId}' to existing '{normalizedId}'");

                    // Add any non-duplicate profiles to the normalized entry
                    foreach (var profile in _gameProfiles[gameId])
                    {
                        if (!_gameProfiles[normalizedId].Any(p => p.Id == profile.Id))
                        {
                            _gameProfiles[normalizedId].Add(profile);
                            madeChanges = true;
                        }
                    }
                }
                else
                {
                    // Just move the profiles to the normalized ID
                    App.LogService?.LogDebug($"Moving {_gameProfiles[gameId].Count} profiles from '{gameId}' to new normalized ID '{normalizedId}'");
                    _gameProfiles[normalizedId] = _gameProfiles[gameId];
                    madeChanges = true;
                }

                // Remove the non-normalized entry
                _gameProfiles.Remove(gameId);

                // Also update the active profile ID if needed
                if (_activeProfileIds.ContainsKey(gameId))
                {
                    string activeId = _activeProfileIds[gameId];
                    _activeProfileIds[normalizedId] = activeId;
                    _activeProfileIds.Remove(gameId);
                    App.LogService?.LogDebug($"Moved active profile ID '{activeId}' from '{gameId}' to '{normalizedId}'");
                    madeChanges = true;
                }
            }

            return madeChanges;
        }

        public async Task MigrateGameProfilesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Starting game profile ID normalization...");

                // First, run the enhanced normalization process
                bool madeChanges = EnsureNormalizedGameIds();

                // If changes were made, save everything
                if (madeChanges)
                {
                    App.LogService?.LogDebug("Changes were made during profile ID normalization - saving profiles");
                    await SaveProfilesAsync();
                }

                App.LogService?.Info("Game profile ID normalization completed successfully");
            }, "Migrate game profiles", true);
        }

        public async Task DeduplicateProfilesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info("Starting profile deduplication");
                bool changesDetected = false;

                // Process each game ID
                foreach (var gameId in _gameProfiles.Keys.ToList())
                {
                    // Skip if there are no profiles or only one profile
                    if (!_gameProfiles.ContainsKey(gameId) || _gameProfiles[gameId].Count <= 1)
                        continue;

                    var profiles = _gameProfiles[gameId];
                    var uniqueProfiles = new Dictionary<string, ModProfile>();
                    var duplicates = new List<ModProfile>();

                    // First pass: Collect default profiles and track duplicates
                    foreach (var profile in profiles)
                    {
                        // Skip null profiles
                        if (profile == null) continue;

                        // If this is a default profile with no mods, consider it a candidate for deduplication
                        if (profile.Name == "Default Profile" &&
                            (profile.AppliedMods == null || profile.AppliedMods.Count == 0))
                        {
                            // If we already saw a default profile, this is a duplicate
                            if (uniqueProfiles.ContainsKey("Default"))
                            {
                                duplicates.Add(profile);
                                App.LogService?.LogDebug($"Found duplicate default profile (ID: {profile.Id})");
                            }
                            else
                            {
                                uniqueProfiles["Default"] = profile;
                            }
                        }
                        else
                        {
                            // For non-default profiles, use the name as key
                            string key = profile.Name ?? "Unknown";

                            if (uniqueProfiles.ContainsKey(key))
                            {
                                // Keep the one with most mods or most recent
                                var existing = uniqueProfiles[key];
                                var existingModCount = existing.AppliedMods?.Count ?? 0;
                                var newModCount = profile.AppliedMods?.Count ?? 0;

                                // If the new one has more mods or is more recent, replace
                                if (newModCount > existingModCount ||
                                    profile.LastModified > existing.LastModified)
                                {
                                    App.LogService?.LogDebug($"Replacing profile '{existing.Name}' with newer/more complete version");
                                    duplicates.Add(existing);
                                    uniqueProfiles[key] = profile;
                                }
                                else
                                {
                                    App.LogService?.LogDebug($"Found duplicate profile: {profile.Name} (ID: {profile.Id})");
                                    duplicates.Add(profile);
                                }
                            }
                            else
                            {
                                uniqueProfiles[key] = profile;
                            }
                        }
                    }

                    // Only proceed if we found duplicates
                    if (duplicates.Count == 0)
                        continue;

                    App.LogService?.Info($"Found {duplicates.Count} duplicate profiles for game {gameId}");
                    changesDetected = true;

                    // Check if active profile is among duplicates
                    string activeId = _activeProfileIds.ContainsKey(gameId) ? _activeProfileIds[gameId] : null;
                    bool needNewActiveProfile = false;

                    if (!string.IsNullOrEmpty(activeId))
                    {
                        needNewActiveProfile = duplicates.Any(p => p.Id == activeId);
                        if (needNewActiveProfile)
                        {
                            App.LogService?.LogDebug("Active profile is among duplicates to be removed");
                        }
                    }

                    // Remove duplicates from the list
                    foreach (var duplicate in duplicates)
                    {
                        profiles.Remove(duplicate);

                        // Delete the duplicate file as well
                        await _storageService.DeleteProfileAsync(gameId, duplicate.Id);
                        App.LogService?.LogDebug($"Deleted duplicate profile: {duplicate.Name} (ID: {duplicate.Id})");
                    }

                    // Update active profile if needed
                    if (needNewActiveProfile && profiles.Count > 0)
                    {
                        _activeProfileIds[gameId] = profiles[0].Id;
                        await _storageService.SetActiveProfileAsync(gameId, profiles[0].Id);
                        App.LogService?.Info($"Updated active profile to: {profiles[0].Name} (ID: {profiles[0].Id})");
                    }
                }

                // Save changes if any duplicates were removed
                if (changesDetected)
                {
                    await SaveProfilesAsync();
                    App.LogService?.Info("Profile deduplication complete - changes saved");
                }
                else
                {
                    App.LogService?.Info("Profile deduplication complete - no duplicates found");
                }
            }, "Deduplicate profiles", true);
        }

        public ModProfile GetFullyLoadedActiveProfile(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"GetFullyLoadedActiveProfile: Loading active profile for game {gameId}");

                // Normalize game ID
                gameId = NormalizeGameId(gameId);

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("Game ID is null or empty");
                    return new ModProfile();
                }

                // Make sure the profiles list exists
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    App.LogService?.LogDebug($"No profiles found in memory for {gameId}, trying to load from storage");
                    // Load profiles from file storage
                    var loadProfilesTask = _storageService.GetProfilesForGameAsync(gameId);
                    loadProfilesTask.Wait();
                    var profiles = loadProfilesTask.Result;

                    if (profiles != null && profiles.Count > 0)
                    {
                        _gameProfiles[gameId] = profiles;
                        App.LogService?.LogDebug($"Loaded {profiles.Count} profiles from storage for {gameId}");
                    }
                    else
                    {
                        App.LogService?.LogDebug($"No profiles found in storage for {gameId}, getting default profiles");
                        GetProfilesForGame(gameId); // This creates a default profile if needed
                    }
                }

                // Get the active profile id
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(gameId))
                {
                    activeProfileId = _activeProfileIds[gameId];
                    App.LogService?.LogDebug($"Found active profile ID in memory: {activeProfileId}");
                }
                else
                {
                    // Try to load from storage
                    var getActiveIdTask = _storageService.GetActiveProfileIdAsync(gameId);
                    getActiveIdTask.Wait();
                    activeProfileId = getActiveIdTask.Result;

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        _activeProfileIds[gameId] = activeProfileId;
                        App.LogService?.LogDebug($"Loaded active profile ID from storage: {activeProfileId}");
                    }
                }

                // Find the active profile
                ModProfile activeProfile = null;
                if (!string.IsNullOrEmpty(activeProfileId) && _gameProfiles.ContainsKey(gameId))
                {
                    activeProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == activeProfileId);
                    if (activeProfile != null)
                    {
                        App.LogService?.LogDebug($"Found active profile: {activeProfile.Name} with {activeProfile.AppliedMods?.Count ?? 0} mods");

                        // Make sure AppliedMods is initialized
                        if (activeProfile.AppliedMods == null)
                        {
                            activeProfile.AppliedMods = new List<AppliedModSetting>();
                        }

                        return activeProfile;
                    }
                    else
                    {
                        App.LogService?.Warning($"Active profile with ID {activeProfileId} not found in memory");
                    }
                }

                // If no active profile found, try loading it directly from storage
                if (!string.IsNullOrEmpty(activeProfileId))
                {
                    var profilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AMO_Launcher", "Profiles", $"{gameId}_{activeProfileId}.json");

                    if (File.Exists(profilePath))
                    {
                        App.LogService?.LogDebug($"Loading active profile directly from file: {profilePath}");
                        var loadProfileTask = _storageService.LoadProfileAsync(profilePath);
                        loadProfileTask.Wait();
                        activeProfile = loadProfileTask.Result;

                        if (activeProfile != null)
                        {
                            App.LogService?.LogDebug($"Successfully loaded profile from file: {activeProfile.Name}");

                            // Add to in-memory collection if not already there
                            if (_gameProfiles.ContainsKey(gameId) &&
                                !_gameProfiles[gameId].Any(p => p.Id == activeProfile.Id))
                            {
                                _gameProfiles[gameId].Add(activeProfile);
                            }

                            // Make sure AppliedMods is initialized
                            if (activeProfile.AppliedMods == null)
                            {
                                activeProfile.AppliedMods = new List<AppliedModSetting>();
                            }

                            return activeProfile;
                        }
                    }
                }

                // If no active profile found, use the first one
                if (_gameProfiles.ContainsKey(gameId) && _gameProfiles[gameId].Count > 0)
                {
                    activeProfile = _gameProfiles[gameId][0];
                    _activeProfileIds[gameId] = activeProfile.Id;

                    // Set this as the active profile in storage
                    var setActiveTask = _storageService.SetActiveProfileAsync(gameId, activeProfile.Id);
                    setActiveTask.Wait();

                    App.LogService?.LogDebug($"Using first profile as active: {activeProfile.Name}");

                    // Make sure AppliedMods is initialized
                    if (activeProfile.AppliedMods == null)
                    {
                        activeProfile.AppliedMods = new List<AppliedModSetting>();
                    }

                    return activeProfile;
                }

                // Still nothing found, create a new default profile
                App.LogService?.Warning("Creating new default profile as last resort");
                var newProfile = new ModProfile();
                newProfile.Id = Guid.NewGuid().ToString();
                newProfile.AppliedMods = new List<AppliedModSetting>();

                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                }

                _gameProfiles[gameId].Add(newProfile);
                _activeProfileIds[gameId] = newProfile.Id;

                // Save this new profile
                var saveProfileTask = _storageService.SaveProfileAsync(gameId, newProfile);
                saveProfileTask.Wait();
                var setActiveIdTask = _storageService.SetActiveProfileAsync(gameId, newProfile.Id);
                setActiveIdTask.Wait();

                return newProfile;
            }, $"Get fully loaded active profile for game {gameId}", true, new ModProfile());
        }
    }
}
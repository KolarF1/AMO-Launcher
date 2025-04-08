using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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
            // Always normalize the game ID first
            gameId = NormalizeGameId(gameId);

            if (string.IsNullOrEmpty(gameId))
            {
                App.LogToFile("GetProfilesForGame: gameId is null or empty, returning empty list");
                return new List<ModProfile>();
            }

            // Log the normalized game ID we're using
            App.LogToFile($"GetProfilesForGame: Looking for profiles for normalized game ID '{gameId}'");

            // First check if we already have profiles in memory
            if (_gameProfiles.ContainsKey(gameId))
            {
                var profiles = _gameProfiles[gameId];
                App.LogToFile($"Found {profiles.Count} profiles in memory for game '{gameId}'");
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
                    App.LogToFile($"Found {profilesFromStorage.Count} profiles in storage for game '{gameId}'");
                    _gameProfiles[gameId] = profilesFromStorage;

                    // Also get the active profile ID
                    var activeIdTask = Task.Run(() => _storageService.GetActiveProfileIdAsync(gameId));
                    activeIdTask.Wait();
                    string activeProfileId = activeIdTask.Result;

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        App.LogToFile($"Found active profile ID in storage: {activeProfileId} for game '{gameId}'");
                        _activeProfileIds[gameId] = activeProfileId;
                    }
                    else if (profilesFromStorage.Count > 0)
                    {
                        // Default to first profile if no active ID
                        _activeProfileIds[gameId] = profilesFromStorage[0].Id;
                        App.LogToFile($"No active profile found, setting first profile as active: {profilesFromStorage[0].Id}");

                        // Save this active profile ID to storage
                        var setActiveTask = Task.Run(() => _storageService.SetActiveProfileAsync(gameId, profilesFromStorage[0].Id));
                        setActiveTask.Wait();
                    }

                    return _gameProfiles[gameId];
                }
                else
                {
                    App.LogToFile($"No profiles found in storage for game '{gameId}'");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error checking storage for existing profiles: {ex.Message}");
                // Continue to create default profile
            }

            // No profiles found in memory or storage, create a default profile
            var defaultProfile = new ModProfile();
            defaultProfile.Id = Guid.NewGuid().ToString();

            App.LogToFile($"Creating new default profile for game '{gameId}' with ID {defaultProfile.Id}");

            // Initialize the collections if needed
            _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
            _activeProfileIds[gameId] = defaultProfile.Id;

            // Save this new profile to persistent storage
            Task.Run(() => {
                _storageService.SaveProfileAsync(gameId, defaultProfile);
                _storageService.SetActiveProfileAsync(gameId, defaultProfile.Id);
                SaveProfilesAsync();
            }).ConfigureAwait(false);

            return _gameProfiles[gameId];
        }

        // Get active profile
        public ModProfile GetActiveProfile(string gameId)
        {
            // Normalize game ID
            gameId = NormalizeGameId(gameId);

            if (string.IsNullOrEmpty(gameId))
            {
                return new ModProfile();
            }

            // Make sure the profiles list exists
            if (!_gameProfiles.ContainsKey(gameId))
            {
                GetProfilesForGame(gameId); // This creates a default profile if needed
            }

            // Get the active profile id
            string activeProfileId = null;
            if (_activeProfileIds.ContainsKey(gameId))
            {
                activeProfileId = _activeProfileIds[gameId];
            }

            // Find the active profile
            if (!string.IsNullOrEmpty(activeProfileId))
            {
                foreach (var profile in _gameProfiles[gameId])
                {
                    if (profile.Id == activeProfileId)
                    {
                        return profile;
                    }
                }
            }

            // If no active profile found, use the first one
            if (_gameProfiles[gameId].Count > 0)
            {
                var firstProfile = _gameProfiles[gameId][0];
                _activeProfileIds[gameId] = firstProfile.Id; // Set as active

                // Save this change
                Task.Run(() => SaveProfilesAsync()).ConfigureAwait(false);

                return firstProfile;
            }

            // Shouldn't reach here normally, but create a new profile if needed
            var newProfile = new ModProfile();
            _gameProfiles[gameId] = new List<ModProfile> { newProfile };
            _activeProfileIds[gameId] = newProfile.Id;

            return newProfile;
        }

        // Set active profile
        public async Task SetActiveProfileAsync(string gameId, string profileId)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
            {
                App.LogToFile("SetActiveProfileAsync: gameId or profileId is null");
                return;
            }

            // Always normalize the game ID
            string normalizedGameId = NormalizeGameId(gameId);

            App.LogToFile($"SetActiveProfileAsync: Setting profile '{profileId}' as active for game '{normalizedGameId}' (original: '{gameId}')");

            // Update in-memory collection with normalized game ID
            _activeProfileIds[normalizedGameId] = profileId;

            // Save active profile ID to file storage
            await _storageService.SetActiveProfileAsync(normalizedGameId, profileId);

            // Verify by trying to read it back
            string savedId = await _storageService.GetActiveProfileIdAsync(normalizedGameId);
            if (savedId == profileId)
            {
                App.LogToFile($"Successfully verified active profile set to '{profileId}'");
            }
            else
            {
                App.LogToFile($"Warning: Failed to verify active profile ID (got '{savedId}' instead of '{profileId}')");
            }
        }

        // Update active profile mods
        public async Task UpdateActiveProfileModsAsync(string gameId, List<AppliedModSetting> mods)
        {
            try
            {
                // Always normalize the game ID
                string normalizedGameId = NormalizeGameId(gameId);

                App.LogToFile($"UpdateActiveProfileModsAsync: Updating mods for game '{normalizedGameId}' (original: '{gameId}')");

                if (string.IsNullOrEmpty(normalizedGameId))
                {
                    App.LogToFile("Game ID is null or empty - cannot update mods");
                    return;
                }

                // Make sure we have the profiles dictionary for this game
                if (!_gameProfiles.ContainsKey(normalizedGameId))
                {
                    App.LogToFile($"No profiles found for game '{normalizedGameId}', loading from storage first");

                    // Try to load from storage before creating new profiles
                    var profiles = await _storageService.GetProfilesForGameAsync(normalizedGameId);
                    if (profiles != null && profiles.Count > 0)
                    {
                        _gameProfiles[normalizedGameId] = profiles;
                        App.LogToFile($"Loaded {profiles.Count} profiles from storage");
                    }
                    else
                    {
                        // Create a default profile if no profiles were found
                        App.LogToFile($"No profiles found in storage, creating default profile");
                        _gameProfiles[normalizedGameId] = new List<ModProfile> { new ModProfile() };
                    }
                }

                // Get the active profile ID - always use normalized game ID
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(normalizedGameId))
                {
                    activeProfileId = _activeProfileIds[normalizedGameId];
                    App.LogToFile($"Found active profile ID: {activeProfileId}");
                }
                else
                {
                    // Try to load active profile ID from storage
                    activeProfileId = await _storageService.GetActiveProfileIdAsync(normalizedGameId);

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        _activeProfileIds[normalizedGameId] = activeProfileId;
                        App.LogToFile($"Loaded active profile ID from storage: {activeProfileId}");
                    }
                    else
                    {
                        App.LogToFile("No active profile ID found for this game");
                    }
                }

                // Find the active profile
                ModProfile activeProfile = null;
                if (!string.IsNullOrEmpty(activeProfileId))
                {
                    activeProfile = _gameProfiles[normalizedGameId].FirstOrDefault(p => p.Id == activeProfileId);
                    if (activeProfile != null)
                    {
                        App.LogToFile($"Found active profile: {activeProfile.Name}");
                    }
                    else
                    {
                        App.LogToFile($"Active profile with ID {activeProfileId} not found");
                    }
                }

                // If no active profile found, use the first one
                if (activeProfile == null && _gameProfiles[normalizedGameId].Count > 0)
                {
                    activeProfile = _gameProfiles[normalizedGameId][0];
                    _activeProfileIds[normalizedGameId] = activeProfile.Id;
                    App.LogToFile($"Using first profile: {activeProfile.Name}");
                }

                // If we have an active profile, update its mods
                if (activeProfile != null)
                {
                    activeProfile.AppliedMods = new List<AppliedModSetting>(mods);
                    activeProfile.LastModified = DateTime.Now;
                    App.LogToFile($"Updated profile '{activeProfile.Name}' with {mods.Count} mods");

                    // Save changes to persistent storage - always use normalized gameId
                    await _storageService.SaveProfileAsync(normalizedGameId, activeProfile);
                    await _storageService.SetActiveProfileAsync(normalizedGameId, activeProfile.Id);

                    // Also save to settings for backward compatibility
                    _configService.SaveAppliedMods(normalizedGameId, mods);
                    App.LogToFile("Saved mods to config service for backward compatibility");
                }
                else
                {
                    App.LogToFile("No active profile available to update");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in UpdateActiveProfileModsAsync: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        // Create a new profile
        public async Task<ModProfile> CreateProfileAsync(string gameId, string profileName)
        {
            try
            {
                // Normalize gameId
                gameId = NormalizeGameId(gameId);

                App.LogToFile($"CreateProfileAsync: Creating profile '{profileName}' for game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileName))
                {
                    App.LogToFile("CreateProfileAsync: gameId or profileName is null");
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
                    App.LogToFile($"Found active profile: {activeProfile?.Name} (ID: {activeId})");

                    if (activeProfile != null)
                    {
                        // Save active profile to storage before creating a new one - don't change it
                        await _storageService.SaveProfileAsync(gameId, activeProfile);
                        App.LogToFile($"Saved current active profile to storage");
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
                App.LogToFile($"Created new profile object with ID: {newProfile.Id} and Name: {profileName}");

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                    App.LogToFile($"Created new profiles collection for game {gameId}");
                }

                // Get current profiles list for debugging
                var existingProfiles = _gameProfiles[gameId];
                App.LogToFile($"Current profiles for game {gameId}:");
                foreach (var p in existingProfiles)
                {
                    App.LogToFile($"  - {p.Name} (ID: {p.Id})");
                }

                // Ensure the name is unique
                string baseName = profileName;
                int counter = 1;

                while (_gameProfiles[gameId].Any(p => p.Name == newProfile.Name))
                {
                    newProfile.Name = $"{baseName} ({counter++})";
                    App.LogToFile($"Renamed profile to ensure uniqueness: {newProfile.Name}");
                }

                // Add the new profile to the collection
                _gameProfiles[gameId].Add(newProfile);
                App.LogToFile($"Added new profile to collection, now have {_gameProfiles[gameId].Count} profiles");

                // IMPORTANT: Do NOT automatically set as active profile here
                // Let the caller explicitly set it as active if needed
                // _activeProfileIds[gameId] = newProfile.Id;

                // Save the new profile to file
                await _storageService.SaveProfileAsync(gameId, newProfile);

                // Save changes to disk immediately
                await SaveProfilesAsync();
                App.LogToFile("Saved profiles to storage");

                // Verify the profile was added properly
                App.LogToFile("Profiles after adding new one:");
                foreach (var p in _gameProfiles[gameId])
                {
                    App.LogToFile($"  - {p.Name} (ID: {p.Id})");
                }

                return newProfile;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in CreateProfileAsync: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // Delete a profile
        public async Task<bool> DeleteProfileAsync(string gameId, string profileId)
        {
            try
            {
                // Normalize gameId
                gameId = NormalizeGameId(gameId);

                App.LogToFile($"DeleteProfileAsync: Deleting profile {profileId} from game {gameId}");

                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogToFile("DeleteProfileAsync: gameId or profileId is null");
                    return false;
                }

                // Check if profiles list exists
                if (!_gameProfiles.TryGetValue(gameId, out var profiles) || profiles == null)
                {
                    App.LogToFile($"DeleteProfileAsync: No profiles found for game {gameId}");
                    return false;
                }

                // Log all profiles before deletion
                App.LogToFile($"Available profiles before deletion:");
                foreach (var profile in profiles)
                {
                    App.LogToFile($"  - {profile.Name} (ID: {profile.Id})");
                }

                // Find the profile
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == profileId);

                if (profileToRemove == null)
                {
                    App.LogToFile($"DeleteProfileAsync: Profile with ID {profileId} not found");
                    return false;
                }

                App.LogToFile($"Found profile to delete: {profileToRemove.Name} (ID: {profileId})");

                // Check if it's the active profile
                if (_activeProfileIds.TryGetValue(gameId, out string activeProfileId) &&
                    activeProfileId == profileId)
                {
                    App.LogToFile("This is the active profile, need to select another one");

                    // If we're removing the active profile, select another one
                    if (profiles.Count > 1)
                    {
                        // Find a different profile to make active
                        var newActiveProfile = profiles.FirstOrDefault(p => p.Id != profileId);
                        if (newActiveProfile != null)
                        {
                            _activeProfileIds[gameId] = newActiveProfile.Id;
                            await _storageService.SetActiveProfileAsync(gameId, newActiveProfile.Id);
                            App.LogToFile($"Set new active profile: {newActiveProfile.Name} (ID: {newActiveProfile.Id})");
                        }
                    }
                    else
                    {
                        App.LogToFile("This is the only profile, creating a new default profile");
                        // If this was the only profile, create a new default
                        var defaultProfile = new ModProfile();
                        _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
                        _activeProfileIds[gameId] = defaultProfile.Id;
                        await _storageService.SetActiveProfileAsync(gameId, defaultProfile.Id);
                        await _storageService.SaveProfileAsync(gameId, defaultProfile);

                        App.LogToFile($"Created new default profile with ID: {defaultProfile.Id}");
                        return true; // We've handled this special case
                    }
                }

                // Remove the profile from memory
                bool removed = profiles.Remove(profileToRemove);
                App.LogToFile($"Profile removed from memory: {removed}");

                // Delete profile file
                bool fileDeleted = await _storageService.DeleteProfileAsync(gameId, profileId);
                App.LogToFile($"Profile file deleted: {fileDeleted}");

                // Log profiles after deletion
                App.LogToFile($"Profiles after deletion:");
                foreach (var profile in profiles)
                {
                    App.LogToFile($"  - {profile.Name} (ID: {profile.Id})");
                }

                // Save changes to disk
                await SaveProfilesAsync();
                App.LogToFile("Saved profiles after deletion");

                return removed;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in DeleteProfileAsync: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Export profile to JSON
        public async Task<bool> ExportProfileAsync(string gameId, string profileId, string exportPath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    return false;
                }

                // Find the profile
                var profile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (profile == null)
                {
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
                }

                // Serialize and save
                var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(exportPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Import profile from JSON
        public async Task<ModProfile> ImportProfileAsync(string gameId, string importPath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    return null;
                }

                // If path not provided, a UI would normally prompt for selection
                if (string.IsNullOrEmpty(importPath))
                {
                    // In a real app, you would show a file picker dialog here
                    return null;
                }

                if (!File.Exists(importPath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(importPath);
                var importedProfile = JsonSerializer.Deserialize<ModProfile>(json);

                if (importedProfile == null)
                {
                    return null;
                }

                // Generate a new ID to avoid conflicts
                importedProfile.Id = Guid.NewGuid().ToString();
                importedProfile.LastModified = DateTime.Now;

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
                }

                // Add the profile
                _gameProfiles[gameId].Add(importedProfile);

                // Save to file storage
                await _storageService.SaveProfileAsync(gameId, importedProfile);

                return importedProfile;
            }
            catch
            {
                return null;
            }
        }

        // Update profile name
        public Task<bool> UpdateProfileNameAsync(string gameId, string profileId, string newName)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    string.IsNullOrEmpty(newName) || !_gameProfiles.ContainsKey(gameId))
                {
                    return Task.FromResult(false);
                }

                var profile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (profile == null)
                {
                    return Task.FromResult(false);
                }

                profile.Name = newName;
                profile.LastModified = DateTime.Now;

                // Save updated profile
                _storageService.SaveProfileAsync(gameId, profile).ConfigureAwait(false);

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        // Create a duplicate of an existing profile
        public async Task<ModProfile> DuplicateProfileAsync(string gameId, string profileId, string newName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    return null;
                }

                var sourceProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (sourceProfile == null)
                {
                    return null;
                }

                // Create new profile with copy of applied mods
                var duplicateProfile = new ModProfile
                {
                    Name = newName ?? $"{sourceProfile.Name} (Copy)",
                    LastModified = DateTime.Now,
                    AppliedMods = sourceProfile.AppliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.ModFolderPath,
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.ArchiveSource,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList()
                };

                _gameProfiles[gameId].Add(duplicateProfile);

                // Save to file storage
                await _storageService.SaveProfileAsync(gameId, duplicateProfile);

                return duplicateProfile;
            }
            catch
            {
                return null;
            }
        }

        // Save profiles to persistent storage
        public async Task SaveProfilesAsync()
        {
            try
            {
                App.LogToFile("Saving profiles to persistent storage");

                // Save profiles for each game
                foreach (var gameId in _gameProfiles.Keys.ToList())  // Create a copy of keys to avoid modification issues
                {
                    try
                    {
                        // Skip empty game IDs
                        if (string.IsNullOrEmpty(gameId))
                        {
                            App.LogToFile("Skipping empty gameId");
                            continue;
                        }

                        // Log detailed info about what we're saving
                        var profiles = _gameProfiles[gameId];

                        // Skip if no profiles to save
                        if (profiles == null || profiles.Count == 0)
                        {
                            App.LogToFile($"No profiles to save for game {gameId}");
                            continue;
                        }

                        App.LogToFile($"Saving {profiles.Count} profiles for game {gameId}");

                        foreach (var profile in profiles)
                        {
                            // Skip null profiles (shouldn't happen but let's be careful)
                            if (profile == null)
                            {
                                App.LogToFile("Skipping null profile");
                                continue;
                            }

                            App.LogToFile($"  - Profile: {profile.Name} (ID: {profile.Id}), Mods: {profile.AppliedMods?.Count ?? 0}");

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
                            App.LogToFile($"Saved active profile ID {activeId} for game {gameId}");
                        }
                        else
                        {
                            App.LogToFile($"No active profile ID for game {gameId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error saving profiles for game {gameId}: {ex.Message}");
                        // Continue with next game
                    }
                }

                App.LogToFile("Profiles saved successfully");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving profiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                // Don't rethrow - we want to keep running even if save fails
            }
        }

        // Load profiles from persistent storage
        public async Task LoadProfilesAsync()
        {
            try
            {
                App.LogToFile("Loading profiles from persistent storage");

                // Clear existing data
                _gameProfiles.Clear();
                _activeProfileIds.Clear();

                // Try loading from file storage first
                bool loadedFromFiles = await LoadProfilesFromFilesAsync();

                if (!loadedFromFiles)
                {
                    // If nothing loaded from files, try loading from settings and migrate
                    App.LogToFile("No profiles loaded from files, trying settings");
                    await LoadProfilesFromSettingsAsync();

                    // Migrate profiles to file storage
                    await MigrateProfilesToFilesAsync();
                }

                // Always normalize game IDs to ensure consistency
                await NormalizeAllGameIdsAsync();

                App.LogToFile($"Loaded profiles for {_gameProfiles.Count} games");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        // Add this method to normalize all game IDs after loading
        private async Task NormalizeAllGameIdsAsync()
        {
            try
            {
                App.LogToFile("Normalizing all game IDs after loading profiles");

                // Use the enhanced normalization
                bool madeChanges = EnsureNormalizedGameIds();

                if (madeChanges)
                {
                    App.LogToFile("Game ID normalization made changes - saving to storage");
                    await SaveProfilesAsync();
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error normalizing game IDs: {ex.Message}");
            }
        }

        // Load profiles from files
        private async Task<bool> LoadProfilesFromFilesAsync()
        {
            try
            {
                // Get all game IDs that have profiles
                var gameIds = await _storageService.GetGameIdsWithProfilesAsync();
                int totalProfilesLoaded = 0;
                bool foundAnyProfiles = false;

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
                        }
                        else if (profiles.Count > 0)
                        {
                            // Default to first profile if no active ID
                            _activeProfileIds[gameId] = profiles[0].Id;
                        }

                        App.LogToFile($"Loaded {profiles.Count} profiles for game {gameId} from files");
                    }
                }

                // Only return true if we found at least one profile and loaded something
                return foundAnyProfiles && totalProfilesLoaded > 0;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles from files: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Load profiles from settings
        private async Task LoadProfilesFromSettingsAsync()
        {
            try
            {
                // Get all game IDs with saved profiles
                var gameIds = await _configService.GetGameIdsWithProfilesAsync();

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
                                }
                                else if (validProfiles.Count > 0)
                                {
                                    // Default to first profile if no active ID saved
                                    _activeProfileIds[gameId] = validProfiles[0].Id;
                                }

                                App.LogToFile($"Loaded {validProfiles.Count} profiles for game {gameId} from settings");
                            }
                        }
                        else
                        {
                            App.LogToFile($"No profiles found in settings for game {gameId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error loading profiles for game {gameId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles from settings: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        // Migrate profiles from settings to files
        private async Task MigrateProfilesToFilesAsync()
        {
            try
            {
                if (_gameProfiles.Count == 0)
                {
                    App.LogToFile("No profiles to migrate");
                    return;
                }

                App.LogToFile($"Migrating {_gameProfiles.Count} games' profiles to files");

                // Migrate profiles and active profile IDs
                await _storageService.MigrateFromSettingsAsync(_gameProfiles, _activeProfileIds);

                App.LogToFile("Migration complete");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error migrating profiles to files: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        public Task<ModProfile> ImportProfileDirectAsync(string gameId, ModProfile importedProfile)
        {
            try
            {
                App.LogToFile($"ImportProfileDirectAsync: Importing profile for game {gameId}");

                if (string.IsNullOrEmpty(gameId) || importedProfile == null)
                {
                    App.LogToFile("ImportProfileDirectAsync: gameId is null or importedProfile is null");
                    return Task.FromResult<ModProfile>(null);
                }

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                    App.LogToFile($"Created new profiles collection for game {gameId}");
                }

                // Add the profile
                _gameProfiles[gameId].Add(importedProfile);
                App.LogToFile($"Added profile {importedProfile.Name} to game {gameId}");

                // Save to file storage
                _storageService.SaveProfileAsync(gameId, importedProfile).ConfigureAwait(false);

                return Task.FromResult(importedProfile);
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in ImportProfileDirectAsync: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return Task.FromResult<ModProfile>(null);
            }
        }

        private string NormalizeGameId(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return gameId;

            // First, trim any whitespace
            gameId = gameId.Trim();

            // Remove any numeric suffix after underscore
            int underscoreIndex = gameId.IndexOf('_');
            if (underscoreIndex > 0)
            {
                // Take only the part before the underscore, ignore everything after
                return gameId.Substring(0, underscoreIndex);
            }

            return gameId;
        }

        // Add this method to consistently normalize game IDs everywhere in the class
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

                App.LogToFile($"Converting game ID from '{gameId}' to normalized form '{normalizedId}'");

                // If profiles already exist for normalized ID, merge them
                if (_gameProfiles.ContainsKey(normalizedId))
                {
                    App.LogToFile($"Merging {_gameProfiles[gameId].Count} profiles from '{gameId}' to existing '{normalizedId}'");

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
                    App.LogToFile($"Moving {_gameProfiles[gameId].Count} profiles from '{gameId}' to new normalized ID '{normalizedId}'");
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
                    App.LogToFile($"Moved active profile ID '{activeId}' from '{gameId}' to '{normalizedId}'");
                    madeChanges = true;
                }
            }

            return madeChanges;
        }

        public async Task MigrateGameProfilesAsync()
        {
            try
            {
                App.LogToFile("Starting game profile ID normalization...");

                // First, run the enhanced normalization process
                bool madeChanges = EnsureNormalizedGameIds();

                // If changes were made, save everything
                if (madeChanges)
                {
                    App.LogToFile("Changes were made during profile ID normalization - saving profiles");
                    await SaveProfilesAsync();
                }

                App.LogToFile("Game profile ID normalization completed successfully");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error during game profile ID normalization: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task DeduplicateProfilesAsync()
        {
            try
            {
                App.LogToFile("Starting profile deduplication");
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
                                    duplicates.Add(existing);
                                    uniqueProfiles[key] = profile;
                                }
                                else
                                {
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

                    App.LogToFile($"Found {duplicates.Count} duplicate profiles for game {gameId}");
                    changesDetected = true;

                    // Check if active profile is among duplicates
                    string activeId = _activeProfileIds.ContainsKey(gameId) ? _activeProfileIds[gameId] : null;
                    bool needNewActiveProfile = false;

                    if (!string.IsNullOrEmpty(activeId))
                    {
                        needNewActiveProfile = duplicates.Any(p => p.Id == activeId);
                    }

                    // Remove duplicates from the list
                    foreach (var duplicate in duplicates)
                    {
                        profiles.Remove(duplicate);

                        // Delete the duplicate file as well
                        await _storageService.DeleteProfileAsync(gameId, duplicate.Id);
                        App.LogToFile($"Deleted duplicate profile: {duplicate.Name} (ID: {duplicate.Id})");
                    }

                    // Update active profile if needed
                    if (needNewActiveProfile && profiles.Count > 0)
                    {
                        _activeProfileIds[gameId] = profiles[0].Id;
                        await _storageService.SetActiveProfileAsync(gameId, profiles[0].Id);
                        App.LogToFile($"Updated active profile to: {profiles[0].Name} (ID: {profiles[0].Id})");
                    }
                }

                // Save changes if any duplicates were removed
                if (changesDetected)
                {
                    await SaveProfilesAsync();
                    App.LogToFile("Profile deduplication complete - changes saved");
                }
                else
                {
                    App.LogToFile("Profile deduplication complete - no duplicates found");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error during profile deduplication: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        public ModProfile GetFullyLoadedActiveProfile(string gameId)
        {
            try
            {
                App.LogToFile($"GetFullyLoadedActiveProfile: Loading active profile for game {gameId}");

                // Normalize game ID
                gameId = NormalizeGameId(gameId);

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogToFile("Game ID is null or empty");
                    return new ModProfile();
                }

                // Make sure the profiles list exists
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    App.LogToFile($"No profiles found in memory for {gameId}, trying to load from storage");
                    // Load profiles from file storage
                    var loadProfilesTask = _storageService.GetProfilesForGameAsync(gameId);
                    loadProfilesTask.Wait();
                    var profiles = loadProfilesTask.Result;

                    if (profiles != null && profiles.Count > 0)
                    {
                        _gameProfiles[gameId] = profiles;
                        App.LogToFile($"Loaded {profiles.Count} profiles from storage for {gameId}");
                    }
                    else
                    {
                        App.LogToFile($"No profiles found in storage for {gameId}, getting default profiles");
                        GetProfilesForGame(gameId); // This creates a default profile if needed
                    }
                }

                // Get the active profile id
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(gameId))
                {
                    activeProfileId = _activeProfileIds[gameId];
                    App.LogToFile($"Found active profile ID in memory: {activeProfileId}");
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
                        App.LogToFile($"Loaded active profile ID from storage: {activeProfileId}");
                    }
                }

                // Find the active profile
                ModProfile activeProfile = null;
                if (!string.IsNullOrEmpty(activeProfileId) && _gameProfiles.ContainsKey(gameId))
                {
                    activeProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == activeProfileId);
                    if (activeProfile != null)
                    {
                        App.LogToFile($"Found active profile: {activeProfile.Name} with {activeProfile.AppliedMods?.Count ?? 0} mods");

                        // Make sure AppliedMods is initialized
                        if (activeProfile.AppliedMods == null)
                        {
                            activeProfile.AppliedMods = new List<AppliedModSetting>();
                        }

                        return activeProfile;
                    }
                    else
                    {
                        App.LogToFile($"Active profile with ID {activeProfileId} not found in memory");
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
                        App.LogToFile($"Loading active profile directly from file: {profilePath}");
                        var loadProfileTask = _storageService.LoadProfileAsync(profilePath);
                        loadProfileTask.Wait();
                        activeProfile = loadProfileTask.Result;

                        if (activeProfile != null)
                        {
                            App.LogToFile($"Successfully loaded profile from file: {activeProfile.Name}");

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

                    App.LogToFile($"Using first profile as active: {activeProfile.Name}");

                    // Make sure AppliedMods is initialized
                    if (activeProfile.AppliedMods == null)
                    {
                        activeProfile.AppliedMods = new List<AppliedModSetting>();
                    }

                    return activeProfile;
                }

                // Still nothing found, create a new default profile
                App.LogToFile("Creating new default profile as last resort");
                var newProfile = new ModProfile();

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
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in GetFullyLoadedActiveProfile: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return new ModProfile(); // Return empty profile in case of error
            }
        }
    }
}
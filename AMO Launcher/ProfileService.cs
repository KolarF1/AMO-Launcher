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
            // Normalize game ID
            gameId = NormalizeGameId(gameId);

            if (string.IsNullOrEmpty(gameId))
            {
                return new List<ModProfile>();
            }

            // Check if we have profiles for this game
            if (!_gameProfiles.ContainsKey(gameId))
            {
                // Create a default profile
                var defaultProfile = new ModProfile();

                // Generate a new ID to avoid conflicts
                defaultProfile.Id = Guid.NewGuid().ToString();

                App.LogToFile($"Creating new default profile for game {gameId} with ID {defaultProfile.Id}");

                _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
                _activeProfileIds[gameId] = defaultProfile.Id;

                // Save this new profile to persistent storage
                Task.Run(() => SaveProfilesAsync()).ConfigureAwait(false);
            }

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
            if (!string.IsNullOrEmpty(gameId) && !string.IsNullOrEmpty(profileId))
            {
                _activeProfileIds[gameId] = profileId;

                // Save active profile ID to file storage
                await _storageService.SetActiveProfileAsync(gameId, profileId);
            }
        }

        // Update active profile mods
        public async Task UpdateActiveProfileModsAsync(string gameId, List<AppliedModSetting> mods)
        {
            try
            {
                App.LogToFile($"UpdateActiveProfileModsAsync: Updating mods for game {gameId}");

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogToFile("Game ID is null or empty - cannot update mods");
                    return;
                }

                // Make sure we have the profiles dictionary for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    App.LogToFile($"No profiles found for game {gameId}, creating default profile");
                    _gameProfiles[gameId] = new List<ModProfile> { new ModProfile() };
                }

                // Get the active profile ID
                string activeProfileId = null;
                if (_activeProfileIds.ContainsKey(gameId))
                {
                    activeProfileId = _activeProfileIds[gameId];
                    App.LogToFile($"Found active profile ID: {activeProfileId}");
                }
                else
                {
                    App.LogToFile("No active profile ID found for this game");
                }

                // Find the active profile
                ModProfile activeProfile = null;
                if (!string.IsNullOrEmpty(activeProfileId))
                {
                    activeProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == activeProfileId);
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
                if (activeProfile == null && _gameProfiles[gameId].Count > 0)
                {
                    activeProfile = _gameProfiles[gameId][0];
                    _activeProfileIds[gameId] = activeProfile.Id;
                    App.LogToFile($"Using first profile: {activeProfile.Name}");
                }

                // If we have an active profile, update its mods
                if (activeProfile != null)
                {
                    activeProfile.AppliedMods = new List<AppliedModSetting>(mods);
                    activeProfile.LastModified = DateTime.Now;
                    App.LogToFile($"Updated profile '{activeProfile.Name}' with {mods.Count} mods");

                    // Save changes to persistent storage
                    await SaveProfilesAsync();

                    // Also save the specific profile to file
                    await _storageService.SaveProfileAsync(gameId, activeProfile);
                }
                else
                {
                    App.LogToFile("No active profile available to update");
                }

                // Also save to config service for backward compatibility
                try
                {
                    _configService.SaveAppliedMods(gameId, mods);
                    App.LogToFile("Saved mods to config service for backward compatibility");
                }
                catch (Exception ex)
                {
                    App.LogToFile($"Error saving to config service: {ex.Message}");
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

                // First, save any changes to the current active profile
                if (_activeProfileIds.ContainsKey(gameId) && _gameProfiles.ContainsKey(gameId))
                {
                    string activeId = _activeProfileIds[gameId];
                    var activeProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == activeId);
                    App.LogToFile($"Saving current active profile '{activeProfile?.Name}' before creating new one");

                    if (activeProfile != null)
                    {
                        await _storageService.SaveProfileAsync(gameId, activeProfile);
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

                // Set as active profile
                _activeProfileIds[gameId] = newProfile.Id;
                App.LogToFile($"Set as active profile: {newProfile.Id}");

                // Save the active profile ID
                await _storageService.SetActiveProfileAsync(gameId, newProfile.Id);

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

                App.LogToFile($"Loaded profiles for {_gameProfiles.Count} games");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
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

                foreach (var gameId in gameIds)
                {
                    // Load profiles for this game
                    var profiles = await _storageService.GetProfilesForGameAsync(gameId);

                    if (profiles != null && profiles.Count > 0)
                    {
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

                return totalProfilesLoaded > 0;
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
            return string.IsNullOrEmpty(gameId) ? gameId : gameId.Trim();
        }
    }
}
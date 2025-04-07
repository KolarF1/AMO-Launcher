using AMO_Launcher.Models;
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

        // Dictionary to store profiles
        private Dictionary<string, List<ModProfile>> _gameProfiles = new Dictionary<string, List<ModProfile>>();

        // Dictionary to track active profiles
        private Dictionary<string, string> _activeProfileIds = new Dictionary<string, string>();

        // Constructor - keep initialization super minimal
        public ProfileService(ConfigurationService configService)
        {
            _configService = configService;
            // Don't do anything complex here that could fail
        }

        // Get profiles for a game
        public List<ModProfile> GetProfilesForGame(string gameId)
        {
            // Simplest possible implementation
            if (string.IsNullOrEmpty(gameId))
            {
                return new List<ModProfile>();
            }

            // Check if we have profiles for this game
            if (!_gameProfiles.ContainsKey(gameId))
            {
                // Create a default profile
                var defaultProfile = new ModProfile();
                _gameProfiles[gameId] = new List<ModProfile> { defaultProfile };
                _activeProfileIds[gameId] = defaultProfile.Id;
            }

            return _gameProfiles[gameId];
        }

        // Get active profile
        public ModProfile GetActiveProfile(string gameId)
        {
            // Simplest possible implementation
            if (string.IsNullOrEmpty(gameId) || !_gameProfiles.ContainsKey(gameId))
            {
                return new ModProfile();
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
                return _gameProfiles[gameId][0];
            }

            // Fallback
            return new ModProfile();
        }

        // Set active profile
        public Task SetActiveProfileAsync(string gameId, string profileId)
        {
            if (!string.IsNullOrEmpty(gameId) && !string.IsNullOrEmpty(profileId))
            {
                _activeProfileIds[gameId] = profileId;
            }
            return Task.CompletedTask;
        }

        // Just pass through to config service for now
        public Task UpdateActiveProfileModsAsync(string gameId, List<AppliedModSetting> mods)
        {
            try
            {
                _configService.SaveAppliedMods(gameId, mods);
            }
            catch
            {
                // Ignore errors
            }
            return Task.CompletedTask;
        }

        // Simplest new profile implementation
        public Task<ModProfile> CreateProfileAsync(string gameId, string profileName)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileName))
                {
                    return Task.FromResult<ModProfile>(null);
                }

                var newProfile = new ModProfile { Name = profileName };

                // Make sure we have a list for this game
                if (!_gameProfiles.ContainsKey(gameId))
                {
                    _gameProfiles[gameId] = new List<ModProfile>();
                }

                // Add the profile
                _gameProfiles[gameId].Add(newProfile);

                return Task.FromResult(newProfile);
            }
            catch
            {
                return Task.FromResult<ModProfile>(null);
            }
        }

        // Implement delete profile method
        public Task<bool> DeleteProfileAsync(string gameId, string profileId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    return Task.FromResult(false);
                }

                // Find the profile
                var profiles = _gameProfiles[gameId];
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == profileId);

                if (profileToRemove == null)
                {
                    return Task.FromResult(false);
                }

                // Check if it's the active profile
                if (_activeProfileIds.TryGetValue(gameId, out string activeProfileId) &&
                    activeProfileId == profileId)
                {
                    // If we're removing the active profile, select another one
                    if (profiles.Count > 1)
                    {
                        // Find a different profile to make active
                        var newActiveProfile = profiles.FirstOrDefault(p => p.Id != profileId);
                        if (newActiveProfile != null)
                        {
                            _activeProfileIds[gameId] = newActiveProfile.Id;
                        }
                    }
                    else
                    {
                        // If this was the only profile, create a new default
                        var defaultProfile = new ModProfile();
                        profiles.Add(defaultProfile);
                        _activeProfileIds[gameId] = defaultProfile.Id;
                    }
                }

                // Remove the profile
                return Task.FromResult(profiles.Remove(profileToRemove));
            }
            catch
            {
                return Task.FromResult(false);
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

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        // Create a duplicate of an existing profile
        public Task<ModProfile> DuplicateProfileAsync(string gameId, string profileId, string newName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId) ||
                    !_gameProfiles.ContainsKey(gameId))
                {
                    return Task.FromResult<ModProfile>(null);
                }

                var sourceProfile = _gameProfiles[gameId].FirstOrDefault(p => p.Id == profileId);
                if (sourceProfile == null)
                {
                    return Task.FromResult<ModProfile>(null);
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
                return Task.FromResult(duplicateProfile);
            }
            catch
            {
                return Task.FromResult<ModProfile>(null);
            }
        }

        // Save profiles to persistent storage
        public async Task SaveProfilesAsync()
        {
            try
            {
                App.LogToFile("Saving profiles to persistent storage");

                foreach (var gameId in _gameProfiles.Keys)
                {
                    // Save the profiles through configuration service
                    await _configService.SaveProfilesAsync(gameId, _gameProfiles[gameId]);

                    // Save active profile ID
                    if (_activeProfileIds.TryGetValue(gameId, out string activeId))
                    {
                        await _configService.SaveActiveProfileIdAsync(gameId, activeId);
                    }
                }

                App.LogToFile("Profiles saved successfully");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving profiles: {ex.Message}");
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

                // Get all game IDs with saved profiles
                var gameIds = await _configService.GetGameIdsWithProfilesAsync();

                foreach (var gameId in gameIds)
                {
                    // Load profiles for this game
                    var profiles = await _configService.LoadProfilesAsync(gameId);
                    if (profiles != null && profiles.Count > 0)
                    {
                        _gameProfiles[gameId] = profiles;

                        // Load active profile ID
                        string activeId = await _configService.LoadActiveProfileIdAsync(gameId);
                        if (!string.IsNullOrEmpty(activeId))
                        {
                            _activeProfileIds[gameId] = activeId;
                        }
                        else if (profiles.Count > 0)
                        {
                            // Default to first profile if no active ID saved
                            _activeProfileIds[gameId] = profiles[0].Id;
                        }
                    }
                }

                App.LogToFile($"Loaded profiles for {_gameProfiles.Count} games");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles: {ex.Message}");
            }
        }
    }
}
using AMO_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AMO_Launcher.Services
{
    public class ProfileStorageService
    {
        private readonly string _profilesDirectoryPath;
        private readonly string _activeProfilesDirectoryPath;

        public ProfileStorageService()
        {
            // Create base directory path
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            // Create profiles directory
            _profilesDirectoryPath = Path.Combine(appDataPath, "Profiles");
            if (!Directory.Exists(_profilesDirectoryPath))
            {
                Directory.CreateDirectory(_profilesDirectoryPath);
            }

            // Create directory for active profile markers
            _activeProfilesDirectoryPath = Path.Combine(appDataPath, "Profiles", "Active");
            if (!Directory.Exists(_activeProfilesDirectoryPath))
            {
                Directory.CreateDirectory(_activeProfilesDirectoryPath);
            }
        }

        public async Task SaveProfileAsync(string gameId, ModProfile profile)
        {
            try
            {
                if (profile == null || string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profile.Id))
                {
                    App.LogToFile($"Cannot save profile - missing gameId or profileId");
                    return;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Generate filename: gameId_profileId.json
                string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profile.Id}.json");

                // Create directory for game profiles if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // Serialize profile to JSON
                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Write to file
                await File.WriteAllTextAsync(filePath, json);
                App.LogToFile($"Saved profile {profile.Name} (ID: {profile.Id}) to file for game {gameId}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving profile to file: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task<ModProfile> LoadProfileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    App.LogToFile($"Profile file not found: {filePath}");
                    return null;
                }

                // Read JSON from file
                string json = await File.ReadAllTextAsync(filePath);

                // Deserialize JSON to profile
                var profile = JsonSerializer.Deserialize<ModProfile>(json);

                // Ensure the profile has all required properties
                if (profile.AppliedMods == null)
                    profile.AppliedMods = new List<AppliedModSetting>();

                if (string.IsNullOrEmpty(profile.Id))
                    profile.Id = Guid.NewGuid().ToString();

                if (string.IsNullOrEmpty(profile.Name))
                    profile.Name = "Default Profile";

                App.LogToFile($"Loaded profile {profile.Name} (ID: {profile.Id}) from {filePath}");
                return profile;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profile from file: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<List<ModProfile>> GetProfilesForGameAsync(string gameId)
        {
            try
            {
                var profiles = new List<ModProfile>();
                string safeGameId = SanitizeForFileName(gameId);

                // Find all profile files for this game
                string searchPattern = $"{safeGameId}_*.json";
                var files = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                // Load each profile
                foreach (var file in files)
                {
                    var profile = await LoadProfileAsync(file);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }

                App.LogToFile($"Found {profiles.Count} profiles for game {gameId}");
                return profiles;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error getting profiles for game: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return new List<ModProfile>();
            }
        }

        public async Task SetActiveProfileAsync(string gameId, string profileId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogToFile($"Cannot set active profile - missing gameId or profileId");
                    return;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Create marker file: gameId.active
                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

                // Write the profile ID to the file
                await File.WriteAllTextAsync(filePath, profileId);
                App.LogToFile($"Set active profile {profileId} for game {gameId}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error setting active profile: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task<string> GetActiveProfileIdAsync(string gameId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    return null;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Check marker file: gameId.active
                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

                if (!File.Exists(filePath))
                {
                    App.LogToFile($"No active profile marker found for game {gameId}");
                    return null;
                }

                // Read the profile ID from the file
                string profileId = await File.ReadAllTextAsync(filePath);
                App.LogToFile($"Found active profile {profileId} for game {gameId}");
                return profileId;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error getting active profile ID: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<bool> DeleteProfileAsync(string gameId, string profileId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    return false;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Find the profile file
                string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profileId}.json");

                if (!File.Exists(filePath))
                {
                    App.LogToFile($"Profile file not found for deletion: {filePath}");
                    return false;
                }

                // Delete the file
                File.Delete(filePath);
                App.LogToFile($"Deleted profile {profileId} for game {gameId}");
                return true;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error deleting profile: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public Task<List<string>> GetGameIdsWithProfilesAsync()
        {
            try
            {
                var gameIds = new HashSet<string>();

                // Find all profile files
                var files = Directory.GetFiles(_profilesDirectoryPath, "*.json");

                // Extract game IDs from filenames
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    int underscoreIndex = fileName.IndexOf('_');

                    if (underscoreIndex > 0)
                    {
                        string gameId = fileName.Substring(0, underscoreIndex);
                        gameIds.Add(gameId);
                    }
                }

                App.LogToFile($"Found profiles for {gameIds.Count} games");
                return Task.FromResult(gameIds.ToList());
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error getting game IDs with profiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                return Task.FromResult(new List<string>());
            }
        }

        public async Task MigrateFromSettingsAsync(Dictionary<string, List<ModProfile>> gameProfiles, Dictionary<string, string> activeProfileIds)
        {
            try
            {
                if (gameProfiles == null)
                {
                    App.LogToFile("No profiles to migrate from settings");
                    return;
                }

                int migratedCount = 0;

                // Save each profile to a file
                foreach (var entry in gameProfiles)
                {
                    string gameId = entry.Key;
                    var profiles = entry.Value;

                    if (string.IsNullOrEmpty(gameId) || profiles == null)
                        continue;

                    // Check if we already have files for this game
                    string safeGameId = SanitizeForFileName(gameId);
                    string searchPattern = $"{safeGameId}_*.json";
                    var existingFiles = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                    // If we already have files, skip migration for this game
                    if (existingFiles.Length > 0)
                    {
                        App.LogToFile($"Skipping migration for game {gameId} - files already exist");
                        continue;
                    }

                    foreach (var profile in profiles)
                    {
                        if (profile == null)
                            continue;

                        await SaveProfileAsync(gameId, profile);
                        migratedCount++;
                    }

                    // Set active profile if we have one
                    if (activeProfileIds != null && activeProfileIds.TryGetValue(gameId, out string activeId))
                    {
                        await SetActiveProfileAsync(gameId, activeId);
                    }
                }

                App.LogToFile($"Migrated {migratedCount} profiles from settings");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error migrating profiles from settings: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        private string SanitizeForFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknown";

            // Replace invalid characters with underscore
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        }
    }
}
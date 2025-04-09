using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
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
        private string _profilesDirectoryPath;
        private string _activeProfilesDirectoryPath;

        public ProfileStorageService()
        {
            // First create the paths directly in the constructor body
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            _profilesDirectoryPath = Path.Combine(appDataPath, "Profiles");
            _activeProfilesDirectoryPath = Path.Combine(appDataPath, "Profiles", "Active");

            // Initialize directories using ErrorHandler
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Using application data path: {appDataPath}");

                // Create profiles directory
                if (!Directory.Exists(_profilesDirectoryPath))
                {
                    App.LogService?.LogDebug($"Creating profiles directory: {_profilesDirectoryPath}");
                    Directory.CreateDirectory(_profilesDirectoryPath);
                }

                // Create directory for active profile markers
                if (!Directory.Exists(_activeProfilesDirectoryPath))
                {
                    App.LogService?.LogDebug($"Creating active profiles directory: {_activeProfilesDirectoryPath}");
                    Directory.CreateDirectory(_activeProfilesDirectoryPath);
                }

                App.LogService?.Info("ProfileStorageService initialized successfully");
            }, "Initializing ProfileStorageService");
        }

        public async Task SaveProfileAsync(string gameId, ModProfile profile)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (profile == null || string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profile.Id))
                {
                    App.LogService?.Warning($"Cannot save profile - missing gameId or profileId");
                    return;
                }

                // Use performance tracking for file operations
                App.LogService?.LogDebug($"Starting profile save operation");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                {
                    // Sanitize game ID for filename
                    string safeGameId = SanitizeForFileName(gameId);

                    // Generate filename: gameId_profileId.json
                    string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profile.Id}.json");

                    // Create directory for game profiles if it doesn't exist
                    App.LogService?.LogDebug($"Ensuring directory exists for profile at {filePath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    // Serialize profile to JSON
                    App.LogService?.LogDebug($"Serializing profile {profile.Name} to JSON");
                    string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    // Write to file
                    App.LogService?.LogDebug($"Writing profile to file: {filePath}");
                    await File.WriteAllTextAsync(filePath, json);
                    stopwatch.Stop();
                    App.LogService?.Info($"Saved profile {profile.Name} (ID: {profile.Id}) for game {gameId}");

                    // Log performance info
                    long elapsedMs = stopwatch.ElapsedMilliseconds;
                    if (elapsedMs > 500)
                    {
                        App.LogService?.Warning($"Profile save operation took {elapsedMs}ms (exceeded threshold of 500ms)");
                    }
                    else
                    {
                        App.LogService?.LogDebug($"Profile save operation completed in {elapsedMs}ms");
                    }
                }
            }, $"Saving profile for game {gameId}");
        }

        public async Task<ModProfile> LoadProfileAsync(string filePath)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (!File.Exists(filePath))
                {
                    App.LogService?.Warning($"Profile file not found: {filePath}");
                    return null;
                }

                App.LogService?.LogDebug($"Loading profile from: {filePath}");

                // Read JSON from file
                string json = await File.ReadAllTextAsync(filePath);

                // Deserialize JSON to profile
                var profile = JsonSerializer.Deserialize<ModProfile>(json);

                // Ensure the profile has all required properties
                if (profile.AppliedMods == null)
                {
                    App.LogService?.LogDebug("Profile has no AppliedMods, initializing empty list");
                    profile.AppliedMods = new List<AppliedModSetting>();
                }

                if (string.IsNullOrEmpty(profile.Id))
                {
                    App.LogService?.Warning("Profile has no ID, generating new GUID");
                    profile.Id = Guid.NewGuid().ToString();
                }

                if (string.IsNullOrEmpty(profile.Name))
                {
                    App.LogService?.Warning("Profile has no Name, setting to 'Default Profile'");
                    profile.Name = "Default Profile";
                }

                App.LogService?.Info($"Loaded profile {profile.Name} (ID: {profile.Id}) from {filePath}");
                return profile;
            }, $"Loading profile from {filePath}", defaultValue: null);
        }

        public async Task<List<ModProfile>> GetProfilesForGameAsync(string gameId)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var profiles = new List<ModProfile>();

                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("Cannot get profiles for empty gameId");
                    return profiles;
                }

                string safeGameId = SanitizeForFileName(gameId);

                // Use debug flow tracking
                App.LogService?.LogDebug($"[FLOW:GetProfiles] START");

                // Find all profile files for this game
                App.LogService?.LogDebug($"[FLOW:GetProfiles] STEP: Searching for profiles");
                string searchPattern = $"{safeGameId}_*.json";
                var files = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                // Log the count of found files
                App.LogService?.LogDebug($"Found {files.Length} profile files for game {gameId}");

                // Load each profile
                App.LogService?.LogDebug($"[FLOW:GetProfiles] STEP: Loading profiles");
                foreach (var file in files)
                {
                    App.LogService?.LogDebug($"Loading profile from file: {file}");
                    var profile = await LoadProfileAsync(file);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }

                App.LogService?.LogDebug($"[FLOW:GetProfiles] END");

                App.LogService?.Info($"Successfully loaded {profiles.Count} profiles for game {gameId}");
                return profiles;
            }, $"Getting profiles for game {gameId}", defaultValue: new List<ModProfile>());
        }

        public async Task SetActiveProfileAsync(string gameId, string profileId)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogService?.Warning($"Cannot set active profile - missing gameId or profileId");
                    return;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Create marker file: gameId.active
                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

                // Write the profile ID to the file
                App.LogService?.LogDebug($"Setting active profile marker at: {filePath}");
                await File.WriteAllTextAsync(filePath, profileId);
                App.LogService?.Info($"Set active profile {profileId} for game {gameId}");
            }, $"Setting active profile for game {gameId}");
        }

        public async Task<string> GetActiveProfileIdAsync(string gameId)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId))
                {
                    App.LogService?.Warning("Cannot get active profile for empty gameId");
                    return null;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Check marker file: gameId.active
                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

                if (!File.Exists(filePath))
                {
                    App.LogService?.LogDebug($"No active profile marker found for game {gameId}");
                    return null;
                }

                // Read the profile ID from the file
                App.LogService?.LogDebug($"Reading active profile marker from: {filePath}");
                string profileId = await File.ReadAllTextAsync(filePath);
                App.LogService?.Info($"Found active profile {profileId} for game {gameId}");
                return profileId;
            }, $"Getting active profile ID for game {gameId}", defaultValue: null);
        }

        public async Task<bool> DeleteProfileAsync(string gameId, string profileId)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(profileId))
                {
                    App.LogService?.Warning("Cannot delete profile - missing gameId or profileId");
                    return false;
                }

                // Sanitize game ID for filename
                string safeGameId = SanitizeForFileName(gameId);

                // Find the profile file
                string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profileId}.json");

                if (!File.Exists(filePath))
                {
                    App.LogService?.Warning($"Profile file not found for deletion: {filePath}");
                    return false;
                }

                // Delete the file
                App.LogService?.LogDebug($"Deleting profile file: {filePath}");
                File.Delete(filePath);
                App.LogService?.Info($"Deleted profile {profileId} for game {gameId}");

                // Check if this was the active profile and clean up if needed
                string activeId = await GetActiveProfileIdAsync(gameId);
                if (activeId == profileId)
                {
                    App.LogService?.LogDebug($"Deleting active profile marker for game {gameId}");
                    string activeFilePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");
                    if (File.Exists(activeFilePath))
                    {
                        File.Delete(activeFilePath);
                        App.LogService?.Info($"Removed active profile marker for deleted profile");
                    }
                }

                return true;
            }, $"Deleting profile for game {gameId}", defaultValue: false);
        }

        public Task<List<string>> GetGameIdsWithProfilesAsync()
        {
            return ErrorHandler.ExecuteSafeAsync(() =>
            {
                var gameIds = new HashSet<string>();

                // Start flow tracking with direct log messages
                App.LogService?.LogDebug("[FLOW:GetGameIds] START");

                // Find all profile files
                App.LogService?.LogDebug("Scanning for game IDs with profiles");
                App.LogService?.LogDebug("[FLOW:GetGameIds] STEP: ScanningFiles");

                var files = Directory.GetFiles(_profilesDirectoryPath, "*.json");

                // Extract game IDs from filenames
                App.LogService?.LogDebug("[FLOW:GetGameIds] STEP: ExtractingIds");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    int underscoreIndex = fileName.IndexOf('_');

                    if (underscoreIndex > 0)
                    {
                        string gameId = fileName.Substring(0, underscoreIndex);
                        gameIds.Add(gameId);
                        App.LogService?.LogDebug($"Found profile for game ID: {gameId}");
                    }
                }

                App.LogService?.LogDebug("[FLOW:GetGameIds] END");
                App.LogService?.Info($"Found profiles for {gameIds.Count} games");
                return Task.FromResult(gameIds.ToList());
            }, "Getting game IDs with profiles", defaultValue: new List<string>());
        }

        public async Task MigrateFromSettingsAsync(Dictionary<string, List<ModProfile>> gameProfiles, Dictionary<string, string> activeProfileIds)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (gameProfiles == null)
                {
                    App.LogService?.Info("No profiles to migrate from settings");
                    return;
                }

                // Create hierarchical logging with context ID
                string contextId = Guid.NewGuid().ToString().Substring(0, 8);
                App.LogService?.Info($"[{contextId}] (ProfileMigration) Starting migration of profiles from settings for {gameProfiles.Count} games");

                int migratedCount = 0;

                // Save each profile to a file
                foreach (var entry in gameProfiles)
                {
                    string gameId = entry.Key;
                    var profiles = entry.Value;

                    if (string.IsNullOrEmpty(gameId) || profiles == null)
                    {
                        App.LogService?.Warning($"[{contextId}] (ProfileMigration) Skipping invalid game entry in migration");
                        continue;
                    }

                    // Check if we already have files for this game
                    string safeGameId = SanitizeForFileName(gameId);
                    string searchPattern = $"{safeGameId}_*.json";
                    var existingFiles = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                    // If we already have files, skip migration for this game
                    if (existingFiles.Length > 0)
                    {
                        App.LogService?.Info($"[{contextId}] (ProfileMigration) Skipping migration for game {gameId} - files already exist");
                        continue;
                    }

                    // Use hierarchical logging for game migration
                    string gameContextId = Guid.NewGuid().ToString().Substring(0, 8);
                    App.LogService?.LogDebug($"[{gameContextId}] (ProfileMigration > Game_{gameId}) Migrating {profiles.Count} profiles for game {gameId}");

                    foreach (var profile in profiles)
                    {
                        if (profile == null)
                        {
                            App.LogService?.Warning($"[{gameContextId}] (ProfileMigration > Game_{gameId}) Skipping null profile during migration");
                            continue;
                        }

                        string profileContextId = Guid.NewGuid().ToString().Substring(0, 8);
                        App.LogService?.LogDebug($"[{profileContextId}] (ProfileMigration > Game_{gameId} > Profile_{profile.Name}) Migrating profile {profile.Name}");

                        await SaveProfileAsync(gameId, profile);
                        migratedCount++;

                        App.LogService?.LogDebug($"[{profileContextId}] (ProfileMigration > Game_{gameId} > Profile_{profile.Name}) Profile migrated successfully");
                    }

                    // Set active profile if we have one
                    if (activeProfileIds != null && activeProfileIds.TryGetValue(gameId, out string activeId))
                    {
                        App.LogService?.LogDebug($"[{gameContextId}] (ProfileMigration > Game_{gameId}) Setting active profile {activeId} for game {gameId}");
                        await SetActiveProfileAsync(gameId, activeId);
                    }

                    App.LogService?.Info($"[{gameContextId}] (ProfileMigration > Game_{gameId}) Completed migration for game {gameId}");
                }

                App.LogService?.Info($"[{contextId}] (ProfileMigration) Migration complete: {migratedCount} profiles migrated from settings");
            }, "Migrating profiles from settings");
        }

        private string SanitizeForFileName(string input)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(input))
                {
                    LogCategorizedError("Attempted to sanitize empty filename", null, ErrorCategory.FileSystem);
                    return "unknown";
                }

                // Replace invalid characters with underscore
                char[] invalidChars = Path.GetInvalidFileNameChars();
                string sanitized = new string(input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());

                App.LogService?.Trace($"Sanitized filename: '{input}' -> '{sanitized}'");
                return sanitized;
            }, "Sanitizing filename", defaultValue: "unknown");
        }

        // Helper method for categorized error logging
        private void LogCategorizedError(string message, Exception ex, ErrorCategory category)
        {
            // Category prefix for the error message
            string categoryPrefix = $"[{category}] ";

            // Basic logging
            App.LogService?.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                // Log exception details in debug mode
                App.LogService?.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService?.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                // Special handling for file system errors
                if (category == ErrorCategory.FileSystem &&
                   (ex is IOException || ex is UnauthorizedAccessException))
                {
                    App.LogService?.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                }

                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    App.LogService?.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }
}
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
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            _profilesDirectoryPath = Path.Combine(appDataPath, "Profiles");
            _activeProfilesDirectoryPath = Path.Combine(appDataPath, "Profiles", "Active");

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Using application data path: {appDataPath}");

                if (!Directory.Exists(_profilesDirectoryPath))
                {
                    App.LogService?.LogDebug($"Creating profiles directory: {_profilesDirectoryPath}");
                    Directory.CreateDirectory(_profilesDirectoryPath);
                }

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

                App.LogService?.LogDebug($"Starting profile save operation");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                {
                    string safeGameId = SanitizeForFileName(gameId);

                    string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profile.Id}.json");

                    App.LogService?.LogDebug($"Ensuring directory exists for profile at {filePath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    App.LogService?.LogDebug($"Serializing profile {profile.Name} to JSON");
                    string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    App.LogService?.LogDebug($"Writing profile to file: {filePath}");
                    await File.WriteAllTextAsync(filePath, json);
                    stopwatch.Stop();
                    App.LogService?.Info($"Saved profile {profile.Name} (ID: {profile.Id}) for game {gameId}");

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

                string json = await File.ReadAllTextAsync(filePath);

                var profile = JsonSerializer.Deserialize<ModProfile>(json);

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

                App.LogService?.LogDebug($"[FLOW:GetProfiles] START");

                App.LogService?.LogDebug($"[FLOW:GetProfiles] STEP: Searching for profiles");
                string searchPattern = $"{safeGameId}_*.json";
                var files = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                App.LogService?.LogDebug($"Found {files.Length} profile files for game {gameId}");

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

                string safeGameId = SanitizeForFileName(gameId);

                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

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

                string safeGameId = SanitizeForFileName(gameId);

                string filePath = Path.Combine(_activeProfilesDirectoryPath, $"{safeGameId}.active");

                if (!File.Exists(filePath))
                {
                    App.LogService?.LogDebug($"No active profile marker found for game {gameId}");
                    return null;
                }

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

                string safeGameId = SanitizeForFileName(gameId);

                string filePath = Path.Combine(_profilesDirectoryPath, $"{safeGameId}_{profileId}.json");

                if (!File.Exists(filePath))
                {
                    App.LogService?.Warning($"Profile file not found for deletion: {filePath}");
                    return false;
                }

                App.LogService?.LogDebug($"Deleting profile file: {filePath}");
                File.Delete(filePath);
                App.LogService?.Info($"Deleted profile {profileId} for game {gameId}");

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

                App.LogService?.LogDebug("[FLOW:GetGameIds] START");

                App.LogService?.LogDebug("Scanning for game IDs with profiles");
                App.LogService?.LogDebug("[FLOW:GetGameIds] STEP: ScanningFiles");

                var files = Directory.GetFiles(_profilesDirectoryPath, "*.json");

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

                string contextId = Guid.NewGuid().ToString().Substring(0, 8);
                App.LogService?.Info($"[{contextId}] (ProfileMigration) Starting migration of profiles from settings for {gameProfiles.Count} games");

                int migratedCount = 0;

                foreach (var entry in gameProfiles)
                {
                    string gameId = entry.Key;
                    var profiles = entry.Value;

                    if (string.IsNullOrEmpty(gameId) || profiles == null)
                    {
                        App.LogService?.Warning($"[{contextId}] (ProfileMigration) Skipping invalid game entry in migration");
                        continue;
                    }

                    string safeGameId = SanitizeForFileName(gameId);
                    string searchPattern = $"{safeGameId}_*.json";
                    var existingFiles = Directory.GetFiles(_profilesDirectoryPath, searchPattern);

                    if (existingFiles.Length > 0)
                    {
                        App.LogService?.Info($"[{contextId}] (ProfileMigration) Skipping migration for game {gameId} - files already exist");
                        continue;
                    }

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

                char[] invalidChars = Path.GetInvalidFileNameChars();
                string sanitized = new string(input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());

                App.LogService?.Trace($"Sanitized filename: '{input}' -> '{sanitized}'");
                return sanitized;
            }, "Sanitizing filename", defaultValue: "unknown");
        }

        private void LogCategorizedError(string message, Exception ex, ErrorCategory category)
        {
            string categoryPrefix = $"[{category}] ";

            App.LogService?.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                App.LogService?.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService?.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                if (category == ErrorCategory.FileSystem &&
                   (ex is IOException || ex is UnauthorizedAccessException))
                {
                    App.LogService?.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                }

                if (ex.InnerException != null)
                {
                    App.LogService?.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }
}
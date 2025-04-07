using AMO_Launcher.Models;
using System;
using System.Collections.Generic;
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

        // Stub methods that won't be used yet
        public Task<bool> DeleteProfileAsync(string gameId, string profileId) => Task.FromResult(false);
        public Task<bool> ExportProfileAsync(string gameId, string profileId) => Task.FromResult(false);
        public Task<ModProfile> ImportProfileAsync(string gameId) => Task.FromResult<ModProfile>(null);
    }
}
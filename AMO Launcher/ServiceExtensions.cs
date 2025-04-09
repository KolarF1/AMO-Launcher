using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Services
{
    public static class ServiceExtensions
    {
        private static GameBackupService _gameBackupService;

        /// <summary>
        /// Gets the singleton instance of GameBackupService with proper error handling
        /// </summary>
        public static GameBackupService GetGameBackupService()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (_gameBackupService == null)
                {
                    if (App.LogService != null)
                    {
                        App.LogService.LogDebug("Creating new GameBackupService instance");
                    }

                    _gameBackupService = new GameBackupService();

                    if (App.LogService != null)
                    {
                        App.LogService.Info("GameBackupService initialized");
                    }
                }

                return _gameBackupService;
            }, "Getting GameBackupService");
        }

        /// <summary>
        /// Asynchronously initializes the GameBackupService if needed
        /// </summary>
        public static async Task<GameBackupService> GetGameBackupServiceAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (_gameBackupService == null)
                {
                    if (App.LogService != null)
                    {
                        App.LogService.LogDebug("Asynchronously creating new GameBackupService instance");
                    }

                    _gameBackupService = new GameBackupService();

                    // If the service has any async initialization method, call it here
                    // await _gameBackupService.InitializeAsync();

                    if (App.LogService != null)
                    {
                        App.LogService.Info("GameBackupService initialized asynchronously");
                    }
                }

                return _gameBackupService;
            }, "Getting GameBackupService asynchronously");
        }
    }
}
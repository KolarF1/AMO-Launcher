using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMO_Launcher.Services
{
    public static class ServiceExtensions
    {
        private static GameBackupService _gameBackupService;

        public static GameBackupService GetGameBackupService()
        {
            if (_gameBackupService == null)
            {
                _gameBackupService = new GameBackupService();
            }

            return _gameBackupService;
        }
    }
}
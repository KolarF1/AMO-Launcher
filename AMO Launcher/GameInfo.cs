using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AMO_Launcher.Models
{
    public class GameInfo : INotifyPropertyChanged
    {
        private string? _name;
        private string? _executablePath;
        private string? _installDirectory;
        private string _version = "Unknown";
        private bool _isDefault;
        private BitmapImage? _icon;
        private string? _id;
        private bool _supportsModding = true;
        private bool _isManuallyAdded = false; // Track if game was manually added

        // Basic game information
        public string? Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ExecutablePath
        {
            get => _executablePath;
            set
            {
                if (_executablePath != value)
                {
                    _executablePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ExecutableName));
                }
            }
        }

        public string? InstallDirectory
        {
            get => _installDirectory;
            set
            {
                if (_installDirectory != value)
                {
                    _installDirectory = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Version
        {
            get => _version;
            set
            {
                if (_version != value)
                {
                    _version = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                if (_isDefault != value)
                {
                    _isDefault = value;
                    OnPropertyChanged();
                }
            }
        }

        public BitmapImage? Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        // Helper to get the name of the executable without the path
        public string? ExecutableName => Path.GetFileName(ExecutablePath);

        // Unique identifier for the game (useful for saving preferences)
        public string? Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        // Additional properties for game-specific information
        public bool SupportsModding
        {
            get => _supportsModding;
            set
            {
                if (_supportsModding != value)
                {
                    _supportsModding = value;
                    OnPropertyChanged();
                }
            }
        }

        // Flag to indicate if this game was manually added by the user
        public bool IsManuallyAdded
        {
            get => _isManuallyAdded;
            set
            {
                if (_isManuallyAdded != value)
                {
                    _isManuallyAdded = value;
                    OnPropertyChanged();
                }
            }
        }

        // Constructor
        public GameInfo()
        {
            // Default values are set in the field initializers
        }

        // Call this after setting Name and ExecutablePath
        public void GenerateId()
        {
            // Use this format consistently everywhere
            string gameName = Name ?? "Unknown";

            // Use a more stable hash format that won't change between app launches
            string executablePathHash = "";
            if (!string.IsNullOrEmpty(ExecutablePath))
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(ExecutablePath);
                    byte[] hashBytes = md5.ComputeHash(pathBytes);
                    executablePathHash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8).ToLowerInvariant();
                }
            }
            else
            {
                executablePathHash = "00000000";
            }

            Id = $"{gameName}_{executablePathHash}";
        }

        public override string ToString()
        {
            return Name ?? "Unknown Game";
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
        #endregion
    }
}
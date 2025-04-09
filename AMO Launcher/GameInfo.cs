using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Models
{
    public class GameInfo : INotifyPropertyChanged, IDisposable
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
        private bool _disposed = false;

        // Basic game information
        public string? Name
        {
            get => _name;
            set
            {
                // Use ErrorHandler to safely update property
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_name != value)
                    {
                        App.LogService?.LogDebug($"Setting Name: '{_name}' -> '{value}'");
                        _name = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(Name)} property", false);
            }
        }

        public string? ExecutablePath
        {
            get => _executablePath;
            set
            {
                // Wrap in ErrorHandler to catch any exceptions during path validation
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_executablePath != value)
                    {
                        if (value != null && !string.IsNullOrEmpty(value))
                        {
                            // Validate the path exists if provided
                            if (!File.Exists(value))
                            {
                                App.LogService?.Warning($"Setting executable path to non-existent file: {value}");
                            }
                        }

                        App.LogService?.LogDebug($"Setting ExecutablePath: '{_executablePath}' -> '{value}'");
                        _executablePath = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(ExecutableName));
                    }
                }, $"Setting {nameof(ExecutablePath)} property", false);
            }
        }

        public string? InstallDirectory
        {
            get => _installDirectory;
            set
            {
                // Use ErrorHandler to safely update property
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_installDirectory != value)
                    {
                        if (value != null && !string.IsNullOrEmpty(value))
                        {
                            // Validate the directory exists if provided
                            if (!Directory.Exists(value))
                            {
                                App.LogService?.Warning($"Setting install directory to non-existent path: {value}");
                            }
                        }

                        App.LogService?.LogDebug($"Setting InstallDirectory: '{_installDirectory}' -> '{value}'");
                        _installDirectory = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(InstallDirectory)} property", false);
            }
        }

        public string Version
        {
            get => _version;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_version != value)
                    {
                        App.LogService?.LogDebug($"Setting Version: '{_version}' -> '{value}'");
                        _version = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(Version)} property", false);
            }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_isDefault != value)
                    {
                        App.LogService?.LogDebug($"Setting IsDefault: '{_isDefault}' -> '{value}'");
                        _isDefault = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(IsDefault)} property", false);
            }
        }

        public BitmapImage? Icon
        {
            get => _icon;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_icon != value)
                    {
                        App.LogService?.LogDebug($"Setting Icon: '{(_icon != null ? "Icon" : "null")}' -> '{(value != null ? "Icon" : "null")}'");
                        _icon = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(Icon)} property", false);
            }
        }

        // Helper to get the name of the executable without the path
        public string? ExecutableName =>
            ErrorHandler.ExecuteSafe<string?>(
                () => Path.GetFileName(ExecutablePath),
                $"Getting {nameof(ExecutableName)}",
                false,
                null
            );

        // Unique identifier for the game (useful for saving preferences)
        public string? Id
        {
            get => _id;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_id != value)
                    {
                        App.LogService?.LogDebug($"Setting Id: '{_id}' -> '{value}'");
                        _id = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(Id)} property", false);
            }
        }

        // Additional properties for game-specific information
        public bool SupportsModding
        {
            get => _supportsModding;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_supportsModding != value)
                    {
                        App.LogService?.LogDebug($"Setting SupportsModding: '{_supportsModding}' -> '{value}'");
                        _supportsModding = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(SupportsModding)} property", false);
            }
        }

        // Flag to indicate if this game was manually added by the user
        public bool IsManuallyAdded
        {
            get => _isManuallyAdded;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_isManuallyAdded != value)
                    {
                        App.LogService?.LogDebug($"Setting IsManuallyAdded: '{_isManuallyAdded}' -> '{value}'");
                        _isManuallyAdded = value;
                        OnPropertyChanged();
                    }
                }, $"Setting {nameof(IsManuallyAdded)} property", false);
            }
        }

        // Constructor
        public GameInfo()
        {
            App.LogService?.LogDebug($"Creating new GameInfo instance");
            // Default values are set in the field initializers

            // Register creation with tracker if using the ObjectTracker pattern
            if (App.LogService?.ShouldLogTrace() == true)
            {
                App.LogService?.Trace($"GameInfo object created with hash: {this.GetHashCode()}");
            }
        }

        // Call this after setting Name and ExecutablePath
        public void GenerateId()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Generating Id for game: {Name}");
                var stopwatch = Stopwatch.StartNew();

                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    // Fallback for games without an executable path
                    string newId = $"unknown_{Guid.NewGuid().ToString().Substring(0, 8)}";
                    App.LogService?.Warning($"Generating Id without ExecutablePath: {newId}");
                    Id = newId;
                    return;
                }

                // Get the file name without extension
                string fileName = Path.GetFileNameWithoutExtension(ExecutablePath);

                // Replace underscores with spaces to normalize the ID
                string normalizedName = fileName.Replace('_', ' ');

                // Use just the normalized name without any additional hash/suffix
                Id = normalizedName;

                stopwatch.Stop();
                App.LogService?.LogDebug($"Id generation completed in {stopwatch.ElapsedMilliseconds}ms: '{Id}'");

            }, "Generating game ID", true);
        }

        /// <summary>
        /// Loads the game icon from the executable file
        /// </summary>
        public void LoadIconFromExecutable()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(ExecutablePath) || !File.Exists(ExecutablePath))
                {
                    App.LogService?.Warning($"Cannot load icon: ExecutablePath is invalid or file does not exist: {ExecutablePath}");
                    return;
                }

                App.LogService?.LogDebug($"Loading icon from executable: {ExecutablePath}");
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    // Icon loading code would go here
                    // This is a placeholder - the actual implementation would depend on your icon extraction logic

                    // Example:
                    // var icon = IconExtractor.Extract(ExecutablePath, 0, true);
                    // Icon = ConvertToImageSource(icon);
                }
                catch (Exception ex)
                {
                    App.LogService?.Error($"Failed to extract icon from executable: {ex.Message}");
                    App.LogService?.LogDebug($"Icon extraction error details: {ex}");
                }

                stopwatch.Stop();
                App.LogService?.LogDebug($"Icon loading completed in {stopwatch.ElapsedMilliseconds}ms");

                // Performance warning if loading is slow
                if (stopwatch.ElapsedMilliseconds > 100)
                {
                    App.LogService?.Warning($"Icon loading for {Name} took longer than expected ({stopwatch.ElapsedMilliseconds}ms)");
                }
            }, "Loading game icon", true);
        }

        /// <summary>
        /// Validates that this GameInfo represents a valid, launchable game
        /// </summary>
        public bool Validate()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Validating GameInfo: {Name}");

                // Check required fields
                if (string.IsNullOrEmpty(Name))
                {
                    App.LogService?.Warning("Validation failed: Name is empty");
                    return false;
                }

                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    App.LogService?.Warning("Validation failed: ExecutablePath is empty");
                    return false;
                }

                if (!File.Exists(ExecutablePath))
                {
                    App.LogService?.Warning($"Validation failed: Executable file does not exist: {ExecutablePath}");
                    return false;
                }

                if (string.IsNullOrEmpty(InstallDirectory))
                {
                    App.LogService?.Warning("Validation failed: InstallDirectory is empty");
                    return false;
                }

                if (!Directory.Exists(InstallDirectory))
                {
                    App.LogService?.Warning($"Validation failed: Install directory does not exist: {InstallDirectory}");
                    return false;
                }

                if (string.IsNullOrEmpty(Id))
                {
                    App.LogService?.Warning("Validation failed: Id is empty, generating one");
                    GenerateId();
                }

                App.LogService?.Info($"Game validation successful: {Name}");
                return true;
            }, "Validating game information", false, false);
        }

        /// <summary>
        /// Creates a deep copy of the GameInfo object
        /// </summary>
        public GameInfo Clone()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Cloning GameInfo: {Name}");

                var clone = new GameInfo
                {
                    Name = this.Name,
                    ExecutablePath = this.ExecutablePath,
                    InstallDirectory = this.InstallDirectory,
                    Version = this.Version,
                    IsDefault = this.IsDefault,
                    Icon = this.Icon, // Note: BitmapImage might need special handling for deep cloning
                    Id = this.Id,
                    SupportsModding = this.SupportsModding,
                    IsManuallyAdded = this.IsManuallyAdded
                };

                App.LogService?.LogDebug($"Successfully cloned GameInfo: {Name}");
                return clone;
            }, "Cloning game information", true, null);
        }

        public override string ToString()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                return Name ?? "Unknown Game";
            }, "Converting game to string", false, "Unknown Game");
        }

        // Implements IDisposable pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    App.LogService?.LogDebug($"Disposing GameInfo: {Name}");

                    try
                    {
                        // For BitmapImage, we don't need to explicitly dispose - just set to null
                        // and let garbage collection handle it
                        if (_icon != null)
                        {
                            App.LogService?.Trace("Releasing BitmapImage icon reference");
                            // Freeze the bitmap to ensure it can be garbage collected properly
                            if (!_icon.IsFrozen && _icon.CanFreeze)
                            {
                                _icon.Freeze();
                            }
                        }

                        _icon = null;
                    }
                    catch (Exception ex)
                    {
                        App.LogService?.Error($"Error while disposing GameInfo resources: {ex.Message}");
                        App.LogService?.LogDebug($"Dispose error details: {ex}");
                    }
                }

                // Record disposal in trace log
                if (App.LogService?.ShouldLogTrace() == true)
                {
                    App.LogService?.Trace($"GameInfo object disposed with hash: {this.GetHashCode()}");
                }

                _disposed = true;
            }
        }

        ~GameInfo()
        {
            // In case Dispose() is not called explicitly
            if (!_disposed && App.LogService != null)
            {
                App.LogService.Warning($"GameInfo finalized without proper disposal: {Name}");
            }
            Dispose(false);
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
            }, $"Raising PropertyChanged for {propertyName}", false);
        }
        #endregion
    }
}
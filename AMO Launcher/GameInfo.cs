using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
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
        private bool _isManuallyAdded = false;
        private bool _disposed = false;

        public string? Name
        {
            get => _name;
            set
            {
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
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_executablePath != value)
                    {
                        if (value != null && !string.IsNullOrEmpty(value))
                        {
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
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_installDirectory != value)
                    {
                        if (value != null && !string.IsNullOrEmpty(value))
                        {
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

        public string? ExecutableName =>
            ErrorHandler.ExecuteSafe<string?>(
                () => Path.GetFileName(ExecutablePath),
                $"Getting {nameof(ExecutableName)}",
                false,
                null
            );

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

        public GameInfo()
        {
            App.LogService?.LogDebug($"Creating new GameInfo instance");

            if (App.LogService?.ShouldLogTrace() == true)
            {
                App.LogService?.Trace($"GameInfo object created with hash: {this.GetHashCode()}");
            }
        }

        public void GenerateId()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Generating Id for game: {Name}");
                var stopwatch = Stopwatch.StartNew();

                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    string newId = $"unknown_{Guid.NewGuid().ToString().Substring(0, 8)}";
                    App.LogService?.Warning($"Generating Id without ExecutablePath: {newId}");
                    Id = newId;
                    return;
                }

                string fileName = Path.GetFileNameWithoutExtension(ExecutablePath);

                string normalizedName = fileName.Replace('_', ' ');

                Id = normalizedName;

                stopwatch.Stop();
                App.LogService?.LogDebug($"Id generation completed in {stopwatch.ElapsedMilliseconds}ms: '{Id}'");

            }, "Generating game ID", true);
        }

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
                }
                catch (Exception ex)
                {
                    App.LogService?.Error($"Failed to extract icon from executable: {ex.Message}");
                    App.LogService?.LogDebug($"Icon extraction error details: {ex}");
                }

                stopwatch.Stop();
                App.LogService?.LogDebug($"Icon loading completed in {stopwatch.ElapsedMilliseconds}ms");

                if (stopwatch.ElapsedMilliseconds > 100)
                {
                    App.LogService?.Warning($"Icon loading for {Name} took longer than expected ({stopwatch.ElapsedMilliseconds}ms)");
                }
            }, "Loading game icon", true);
        }

        public bool Validate()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Validating GameInfo: {Name}");

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
                    Icon = this.Icon,
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
                    App.LogService?.LogDebug($"Disposing GameInfo: {Name}");

                    try
                    {
                        if (_icon != null)
                        {
                            App.LogService?.Trace("Releasing BitmapImage icon reference");
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

                if (App.LogService?.ShouldLogTrace() == true)
                {
                    App.LogService?.Trace($"GameInfo object disposed with hash: {this.GetHashCode()}");
                }

                _disposed = true;
            }
        }

        ~GameInfo()
        {
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
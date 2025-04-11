using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Models
{
    public class ModInfo : INotifyPropertyChanged
    {
        #region Private Fields
        private string _name = "Unknown Mod";
        private string _description = "";
        private string _version = "N/A";
        private string _author = "Unknown";
        private string _game = "";
        private BitmapImage _icon;
        private string _modFolderPath;
        private string _modFilesPath;
        private bool _isApplied;
        private bool _isActive;
        private int _priority;
        private string _priorityDisplay;
        private string _category = "Uncategorized";
        private bool _isExpanded = false;
        #endregion

        #region Public Properties
        public bool IsFromArchive { get; set; }
        public string ArchiveSource { get; set; }
        public string ArchiveRootPath { get; set; }

        public string Name
        {
            get => _name;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_name != value)
                    {
                        App.LogService?.LogDebug($"Changing mod name from '{_name}' to '{value}'");
                        _name = value;
                        OnPropertyChanged();
                    }
                }, $"Setting Name property for mod {_name}", false);
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_description != value)
                    {
                        if (App.LogService?.ShouldLogDebug() == true)
                        {
                            if (string.IsNullOrEmpty(_description) || string.IsNullOrEmpty(value))
                            {
                                App.LogService.LogDebug($"Setting description for mod '{_name}' from " +
                                    $"{(string.IsNullOrEmpty(_description) ? "empty" : "populated")} to " +
                                    $"{(string.IsNullOrEmpty(value) ? "empty" : "populated")}");
                            }
                            else if (_description.Length != value.Length)
                            {
                                App.LogService.LogDebug($"Updating description for mod '{_name}' (length change: {_description.Length} -> {value.Length})");
                            }
                        }

                        _description = value;
                        OnPropertyChanged();
                    }
                }, $"Setting Description property for mod {_name}", false);
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
                        App.LogService?.LogDebug($"Updating version for mod '{_name}': {_version} -> {value}");
                        _version = value;
                        OnPropertyChanged();
                    }
                }, $"Setting Version property for mod {_name}", false);
            }
        }

        public string Author
        {
            get => _author;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_author != value)
                    {
                        App.LogService?.LogDebug($"Updating author for mod '{_name}': {_author} -> {value}");
                        _author = value;
                        OnPropertyChanged();

                        OnPropertyChanged(nameof(AuthorDisplay));
                    }
                }, $"Setting Author property for mod {_name}", false);
            }
        }

        public string Game
        {
            get => _game;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_game != value)
                    {
                        App.LogService?.LogDebug($"Changing game association for mod '{_name}': {_game} -> {value}");
                        _game = value;
                        OnPropertyChanged();
                    }
                }, $"Setting Game property for mod {_name}", false);
            }
        }

        public BitmapImage Icon
        {
            get => _icon;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_icon != value)
                    {
                        App.LogService?.LogDebug($"Updating icon for mod '{_name}'");
                        _icon = value;
                        OnPropertyChanged();
                    }
                }, $"Setting Icon property for mod {_name}", false);
            }
        }

        public string ModFolderPath
        {
            get => _modFolderPath;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_modFolderPath != value)
                    {
                        if (App.LogService?.ShouldLogDebug() == true)
                        {
                            bool oldPathExists = !string.IsNullOrEmpty(_modFolderPath) && Directory.Exists(_modFolderPath);
                            bool newPathExists = !string.IsNullOrEmpty(value) && Directory.Exists(value);

                            App.LogService.LogDebug($"Changing mod folder path for '{_name}': " +
                                $"{_modFolderPath ?? "null"} (exists: {oldPathExists}) -> " +
                                $"{value ?? "null"} (exists: {newPathExists})");

                            if (!newPathExists && !string.IsNullOrEmpty(value))
                            {
                                App.LogService.Warning($"Setting mod folder path to non-existent directory: {value}");
                            }
                        }

                        _modFolderPath = value;
                        OnPropertyChanged();
                    }
                }, $"Setting ModFolderPath property for mod {_name}", false);
            }
        }

        public string ModFilesPath
        {
            get => _modFilesPath;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_modFilesPath != value)
                    {
                        if (App.LogService?.ShouldLogDebug() == true)
                        {
                            bool oldPathExists = !string.IsNullOrEmpty(_modFilesPath) && Directory.Exists(_modFilesPath);
                            bool newPathExists = !string.IsNullOrEmpty(value) && Directory.Exists(value);

                            App.LogService.LogDebug($"Changing mod files path for '{_name}': " +
                                $"{_modFilesPath ?? "null"} (exists: {oldPathExists}) -> " +
                                $"{value ?? "null"} (exists: {newPathExists})");

                            if (!newPathExists && !string.IsNullOrEmpty(value))
                            {
                                App.LogService.Warning($"Setting mod files path to non-existent directory: {value}");
                            }
                        }

                        _modFilesPath = value;
                        OnPropertyChanged();
                    }
                }, $"Setting ModFilesPath property for mod {_name}", false);
            }
        }

        public bool IsApplied
        {
            get => _isApplied;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_isApplied != value)
                    {
                        App.LogService?.LogDebug($"Changing applied state for mod '{_name}': {_isApplied} -> {value}");
                        _isApplied = value;
                        OnPropertyChanged();
                    }
                }, $"Setting IsApplied property for mod {_name}", false);
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_isActive != value)
                    {
                        App.LogService?.Info($"Changing active state for mod '{_name}': {_isActive} -> {value}");
                        _isActive = value;
                        OnPropertyChanged();
                    }
                }, $"Setting IsActive property for mod {_name}", false);
            }
        }

        public string AuthorDisplay => $"by {Author}";

        public int Priority
        {
            get => _priority;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_priority != value)
                    {
                        App.LogService?.LogDebug($"Changing priority for mod '{_name}': {_priority} -> {value}");
                        _priority = value;
                        OnPropertyChanged();

                        UpdatePriorityDisplay();
                    }
                }, $"Setting Priority property for mod {_name}", false);
            }
        }

        public string PriorityDisplay
        {
            get => _priorityDisplay;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_priorityDisplay != value)
                    {
                        App.LogService?.LogDebug($"Changing priority display for mod '{_name}': {_priorityDisplay} -> {value}");
                        _priorityDisplay = value;
                        OnPropertyChanged();
                    }
                }, $"Setting PriorityDisplay property for mod {_name}", false);
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_category != value)
                    {
                        string newCategory = string.IsNullOrWhiteSpace(value) ? "Uncategorized" : value;
                        App.LogService?.LogDebug($"Changing category for mod '{_name}': {_category} -> {newCategory}");
                        _category = newCategory;
                        OnPropertyChanged();
                    }
                }, $"Setting Category property for mod {_name}", false);
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    if (_isExpanded != value)
                    {
                        App.LogService?.Trace($"Changing expanded state for mod '{_name}': {_isExpanded} -> {value}");
                        _isExpanded = value;
                        OnPropertyChanged();
                    }
                }, $"Setting IsExpanded property for mod {_name}", false);
            }
        }
        #endregion

        #region Methods
        private void UpdatePriorityDisplay()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"Updating priority display for mod '{_name}' with priority {_priority}");
                PriorityDisplay = $"Priority: {_priority}";
            }, $"Updating priority display for mod {_name}", false);
        }

        public ModInfo Clone()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Cloning mod '{_name}'");

                ModInfo clone = new ModInfo
                {
                    Name = this.Name,
                    Description = this.Description,
                    Version = this.Version,
                    Author = this.Author,
                    Game = this.Game,
                    Icon = this.Icon,
                    ModFolderPath = this.ModFolderPath,
                    ModFilesPath = this.ModFilesPath,
                    IsApplied = this.IsApplied,
                    IsActive = this.IsActive,
                    Priority = this.Priority,
                    Category = this.Category,
                    IsExpanded = this.IsExpanded,
                    IsFromArchive = this.IsFromArchive,
                    ArchiveSource = this.ArchiveSource,
                    ArchiveRootPath = this.ArchiveRootPath
                };

                return clone;
            }, $"Cloning mod {_name}", true, null);
        }

        public bool TryLoadIcon(string iconPath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(iconPath))
                {
                    App.LogService?.LogDebug($"Cannot load icon for mod '{_name}': iconPath is null or empty");
                    return false;
                }

                if (!File.Exists(iconPath))
                {
                    App.LogService?.LogDebug($"Icon file not found for mod '{_name}': {iconPath}");
                    return false;
                }

                try
                {
                    App.LogService?.LogDebug($"Loading icon for mod '{_name}' from path: {iconPath}");

                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    Icon = bitmap;
                    App.LogService?.LogDebug($"Successfully loaded icon for mod '{_name}'");
                    return true;
                }
                catch (Exception ex)
                {
                    App.LogService?.Error($"Failed to load icon for mod '{_name}' from path {iconPath}: {ex.Message}");
                    App.LogService?.LogDebug($"Icon load exception details: {ex}");
                    return false;
                }
            }, $"Loading icon for mod {_name}", false, false);
        }

        public bool Validate()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Validating mod '{_name}'");

                List<string> issues = new List<string>();

                if (string.IsNullOrWhiteSpace(Name) || Name == "Unknown Mod")
                {
                    issues.Add("Missing mod name");
                }

                if (string.IsNullOrWhiteSpace(Game))
                {
                    issues.Add("Missing game association");
                }

                if (IsFromArchive)
                {
                    if (string.IsNullOrWhiteSpace(ArchiveSource))
                    {
                        issues.Add("Missing archive source");
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(ModFolderPath))
                    {
                        issues.Add("Missing mod folder path");
                    }
                    else if (!Directory.Exists(ModFolderPath))
                    {
                        issues.Add($"Mod folder does not exist: {ModFolderPath}");
                    }

                    if (string.IsNullOrWhiteSpace(ModFilesPath))
                    {
                        issues.Add("Missing mod files path");
                    }
                    else if (!Directory.Exists(ModFilesPath))
                    {
                        issues.Add($"Mod files directory does not exist: {ModFilesPath}");
                    }
                }

                if (issues.Count > 0)
                {
                    App.LogService?.Warning($"Validation failed for mod '{_name}' with {issues.Count} issues:");
                    foreach (var issue in issues)
                    {
                        App.LogService?.LogDebug($"  - {issue}");
                    }
                    return false;
                }

                App.LogService?.LogDebug($"Mod '{_name}' passed validation");
                return true;
            }, $"Validating mod {_name}", false, false);
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                App.LogService?.Trace($"Property changed for mod '{_name}': {propertyName}");
            }, $"PropertyChanged notification for {propertyName} in mod {_name}", false);
        }
        #endregion
    }

    public class ModFileConflict
    {
        public string FilePath { get; set; }
        public List<ModInfo> ConflictingMods { get; set; } = new List<ModInfo>();

        public void LogConflictDetails()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (ConflictingMods == null || ConflictingMods.Count < 2)
                {
                    App.LogService?.Warning($"Attempted to log conflict details with insufficient conflicting mods for file: {FilePath}");
                    return;
                }

                string modNames = string.Join(", ", ConflictingMods.ConvertAll(m => m.Name));
                App.LogService?.Warning($"File conflict detected: {FilePath}");
                App.LogService?.LogDebug($"Conflicting mods for {FilePath}: {modNames}");

                ConflictingMods.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                var winningMod = ConflictingMods[0];
                App.LogService?.LogDebug($"Winning mod for conflict {FilePath}: {winningMod.Name} (Priority: {winningMod.Priority})");
            }, $"Logging conflict details for {FilePath}", false);
        }
    }

    public class ConflictItem
    {
        public ModInfo Mod { get; set; }
        public string FilePath { get; set; }
        public BitmapImage ModIcon => Mod?.Icon;
        public string ModName => Mod?.Name ?? "Unknown Mod";
        public bool IsWinningMod { get; set; }

        public Brush RowBackground => IsWinningMod ?
            new SolidColorBrush(Color.FromArgb(50, 0, 180, 0)) :
            Brushes.Transparent;

        public void LogConflictItemInfo()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Conflict item: {FilePath}, Mod: {ModName}, IsWinningMod: {IsWinningMod}");
            }, $"Logging conflict item info for {FilePath}", false);
        }
    }
}
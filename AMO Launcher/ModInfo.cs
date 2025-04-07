using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMO_Launcher.Models
{
    public class ModInfo : INotifyPropertyChanged
    {
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
        public bool IsFromArchive { get; set; }
        public string ArchiveSource { get; set; }
        public string ArchiveRootPath { get; set; }

        public string Name
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

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
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

        public string Author
        {
            get => _author;
            set
            {
                if (_author != value)
                {
                    _author = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Game
        {
            get => _game;
            set
            {
                if (_game != value)
                {
                    _game = value;
                    OnPropertyChanged();
                }
            }
        }

        public BitmapImage Icon
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

        public string ModFolderPath
        {
            get => _modFolderPath;
            set
            {
                if (_modFolderPath != value)
                {
                    _modFolderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ModFilesPath
        {
            get => _modFilesPath;
            set
            {
                if (_modFilesPath != value)
                {
                    _modFilesPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsApplied
        {
            get => _isApplied;
            set
            {
                if (_isApplied != value)
                {
                    _isApplied = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AuthorDisplay => $"by {Author}";

        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PriorityDisplay
        {
            get => _priorityDisplay;
            set
            {
                if (_priorityDisplay != value)
                {
                    _priorityDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _category = "Uncategorized";

        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = string.IsNullOrWhiteSpace(value) ? "Uncategorized" : value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    // Represents a conflict between multiple mods for a single file
    public class ModFileConflict
    {
        public string FilePath { get; set; }
        public List<ModInfo> ConflictingMods { get; set; } = new List<ModInfo>();
    }

    // Used for displaying conflicts in the UI
    public class ConflictItem
    {
        public ModInfo Mod { get; set; }
        public string FilePath { get; set; }
        public BitmapImage ModIcon => Mod?.Icon;
        public string ModName => Mod?.Name ?? "Unknown Mod";
        public bool IsWinningMod { get; set; }

        // Visual indicator for which mod takes precedence
        public Brush RowBackground => IsWinningMod ?
            new SolidColorBrush(Color.FromArgb(50, 0, 180, 0)) :
            Brushes.Transparent;
    }
}
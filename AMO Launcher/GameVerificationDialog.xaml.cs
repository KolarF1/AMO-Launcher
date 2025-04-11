using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
using System;
using System.Windows;
using System.Windows.Input;

namespace AMO_Launcher.Views
{
    public partial class GameVerificationDialog : Window
    {
        private GameInfo _game;
        private bool _isVersionChange;

        public bool UserConfirmed { get; private set; } = false;

        public GameVerificationDialog(GameInfo game, bool isVersionChange = false)
        {
            InitializeComponent();

            _game = game;
            _isVersionChange = isVersionChange;

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"Opening game verification dialog for game: {game?.Name ?? "unknown"}");
                App.LogService?.LogDebug($"Dialog parameters - Game: {game?.Name}, IsVersionChange: {isVersionChange}");

                ConfigureDialogContent();
            }, "Initializing game verification dialog", true);
        }

        private void ConfigureDialogContent()
        {
            if (_isVersionChange)
            {
                App.LogService?.LogDebug("Configuring dialog for version change scenario");
                TitleTextBlock.Text = "Game Version Changed";
                MessageTextBlock.Text = $"The version of {_game.Name} has changed. To ensure mods work correctly, the launcher needs to re-create the Original Game Data backup.\n\nPlease verify your game files through Steam/Epic/EA first, then click Continue.";
            }
            else
            {
                App.LogService?.LogDebug("Configuring dialog for standard verification scenario");
                TitleTextBlock.Text = "Game Verification Required";
                MessageTextBlock.Text = $"Before you can use mods with {_game.Name}, the launcher needs to create a backup of the original game files.\n\nPlease verify your game files through Steam/Epic/EA first, then click Continue.";
            }

            GameNameTextBlock.Text = _game?.Name ?? "Unknown Game";

            if (_game?.Icon != null)
            {
                App.LogService?.LogDebug("Setting game icon in verification dialog");
                GameIconImage.Source = _game.Icon;
            }
            else
            {
                App.LogService?.LogDebug("No game icon available to display");
            }

            App.LogService?.LogDebug("Game verification dialog initialized successfully");
        }

        public void CustomizeForReset()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"Customizing verification dialog for reset operation: {_game?.Name}");

                if (_game == null)
                {
                    App.LogService?.Warning("CustomizeForReset called with null game reference");
                    return;
                }

                TitleTextBlock.Text = "Reset Original Game Data";
                MessageTextBlock.Text = $"You are about to reset the Original Game Data backup for {_game.Name}.\n\nBefore continuing, please verify your game files through Steam/Epic/EA to ensure you have clean, unmodified files.\n\nClick Continue when you're ready to proceed with the reset.";

                App.LogService?.LogDebug("Dialog customized for reset operation");
            }, "Customizing dialog for reset operation", true);
        }

        public ICommand DragMoveCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace("Executing DragMove command");
                DragMove();
            }, "Dragging verification dialog window", false);
        });

        public class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                ErrorHandler.ExecuteSafe(() =>
                {
                    _execute();
                }, "Executing relay command", false);
            }

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }

        public ICommand CancelCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"User canceled game verification for {_game?.Name}");
                UserConfirmed = false;
                App.LogService?.LogDebug("Setting UserConfirmed = false and closing dialog");
                Close();
            }, "Handling verification dialog cancel", true);
        });

        public ICommand ContinueCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"User confirmed game verification for {_game?.Name}");
                UserConfirmed = true;
                App.LogService?.LogDebug("Setting UserConfirmed = true and closing dialog");
                Close();
            }, "Handling verification dialog continue", true);
        });
    }
}
using AMO_Launcher.Models;
using System;
using System.Windows;
using System.Windows.Input;

namespace AMO_Launcher.Views
{
    public partial class GameVerificationDialog : Window
    {
        public bool UserConfirmed { get; private set; } = false;
        private readonly GameInfo _game;
        private readonly bool _isVersionChange;

        public GameVerificationDialog(GameInfo game, bool isVersionChange = false)
        {
            InitializeComponent();
            _game = game;
            _isVersionChange = isVersionChange;

            // Set dialog content based on context
            if (isVersionChange)
            {
                TitleTextBlock.Text = "Game Version Changed";
                MessageTextBlock.Text = $"The version of {game.Name} has changed. To ensure mods work correctly, the launcher needs to re-create the Original Game Data backup.\n\nPlease verify your game files through Steam/Epic/EA first, then click Continue.";
            }
            else
            {
                TitleTextBlock.Text = "Game Verification Required";
                MessageTextBlock.Text = $"Before you can use mods with {game.Name}, the launcher needs to create a backup of the original game files.\n\nPlease verify your game files through Steam/Epic/EA first, then click Continue.";
            }

            GameNameTextBlock.Text = game.Name;

            // Set the game icon if available
            if (game.Icon != null)
            {
                GameIconImage.Source = game.Icon;
            }
        }

        // New method to customize the dialog for the Reset operation
        public void CustomizeForReset()
        {
            TitleTextBlock.Text = "Reset Original Game Data";
            MessageTextBlock.Text = $"You are about to reset the Original Game Data backup for {_game.Name}.\n\nBefore continuing, please verify your game files through Steam/Epic/EA to ensure you have clean, unmodified files.\n\nClick Continue when you're ready to proceed with the reset.";
        }

        // Command for dragging the window
        public ICommand DragMoveCommand => new RelayCommand(() => DragMove());

        // Relay command implementation
        public class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _execute();

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }

        // Command for canceling/closing
        public ICommand CancelCommand => new RelayCommand(() =>
        {
            UserConfirmed = false;
            Close();
        });

        // Command for continuing
        public ICommand ContinueCommand => new RelayCommand(() =>
        {
            UserConfirmed = true;
            Close();
        });
    }
}
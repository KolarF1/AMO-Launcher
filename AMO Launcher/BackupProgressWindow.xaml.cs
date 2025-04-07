using AMO_Launcher.Models;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AMO_Launcher.Views
{
    public partial class BackupProgressWindow : Window
    {
        public BackupProgressWindow()
        {
            InitializeComponent();
        }

        public void SetGame(GameInfo game)
        {
            // Use Dispatcher to ensure UI updates happen on the UI thread
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                GameNameTextBlock.Text = game.Name;

                if (game.Icon != null)
                {
                    GameIconImage.Source = game.Icon;
                }
            }));
        }

        // Set the window title and header based on the operation type
        public void SetOperationType(string operationType)
        {
            // Use Dispatcher to ensure UI updates happen on the UI thread
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // Update window title and header text
                this.Title = operationType;
                var titleElement = this.FindName("TitleTextBlock") as System.Windows.Controls.TextBlock;
                if (titleElement != null)
                {
                    titleElement.Text = operationType;
                }

                // Update the operation text
                if (OperationTextBlock != null)
                {
                    OperationTextBlock.Text = $"{operationType} in progress...";
                }
            }));
        }

        public void UpdateProgress(double progress, string statusMessage)
        {
            // Use Dispatcher to ensure UI updates happen on the UI thread
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // Update progress bar (0.0 to 1.0)
                ProgressBar.Value = progress * 100;

                // Update status message
                StatusTextBlock.Text = statusMessage;
            }));
        }

        public void SetCompleted()
        {
            // Use Dispatcher to ensure UI updates happen on the UI thread
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // Update UI to show completion
                StatusTextBlock.Text = "Operation completed successfully!";
                ProgressBar.Value = 100;

                // Change the close button text
                CloseButton.Content = "Close";
                CloseButton.IsEnabled = true;

                // Add completion effect
                CompletedIcon.Visibility = Visibility.Visible;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.LightGreen);
            }));
        }

        public void ShowError(string errorMessage)
        {
            // Use Dispatcher to ensure UI updates happen on the UI thread
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // Update UI to show error
                StatusTextBlock.Text = $"Error: {errorMessage}";

                // Change progress bar color to red
                ProgressBar.Foreground = new SolidColorBrush(Colors.Red);

                // Enable close button
                CloseButton.Content = "Close";
                CloseButton.IsEnabled = true;

                // Show error icon
                ErrorIcon.Visibility = Visibility.Visible;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }));
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

        // Command for closing the window
        public ICommand CloseCommand => new RelayCommand(() => Close());
    }
}
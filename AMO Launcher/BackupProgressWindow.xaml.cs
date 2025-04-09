using AMO_Launcher.Models;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AMO_Launcher.Views
{
    public partial class BackupProgressWindow : Window
    {
        // Track operation context for logging
        private string _operationId;
        private string _operationType;

        public BackupProgressWindow()
        {
            // Generate a unique operation ID for tracking this window instance
            _operationId = $"BackupOp_{DateTime.Now:yyyyMMdd_HHmmss}";

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Creating backup progress window");
                InitializeComponent();
                App.LogService?.LogDebug($"[{_operationId}] Backup progress window initialized");
            }, "Initialize backup progress window");
        }

        public void SetGame(GameInfo game)
        {
            if (game == null)
            {
                App.LogService?.Warning($"[{_operationId}] Attempted to set null game");
                return;
            }

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"[{_operationId}] Setting game: {game.Name}");

                // Use Dispatcher to ensure UI updates happen on the UI thread
                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    GameNameTextBlock.Text = game.Name;

                    if (game.Icon != null)
                    {
                        GameIconImage.Source = game.Icon;
                        App.LogService?.Trace($"[{_operationId}] Game icon set successfully");
                    }
                    else
                    {
                        App.LogService?.Trace($"[{_operationId}] Game has no icon");
                    }
                }));

                App.LogService?.LogDebug($"[{_operationId}] Game set to: {game.Name}");
            }, $"Set game in backup progress window", true);
        }

        // Set the window title and header based on the operation type
        public void SetOperationType(string operationType)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Setting operation type: {operationType}");
                _operationType = operationType;

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
                    else
                    {
                        App.LogService?.Warning($"[{_operationId}] TitleTextBlock not found");
                    }

                    // Update the operation text
                    if (OperationTextBlock != null)
                    {
                        OperationTextBlock.Text = $"{operationType} in progress...";
                    }
                    else
                    {
                        App.LogService?.Warning($"[{_operationId}] OperationTextBlock not found");
                    }
                }));

                App.LogService?.LogDebug($"[{_operationId}] Operation type set successfully");
            }, $"Set operation type in backup progress window", true);
        }

        public void UpdateProgress(double progress, string statusMessage)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"[{_operationId}] Updating progress: {progress:P0}, message: {statusMessage}");

                // Use Dispatcher to ensure UI updates happen on the UI thread
                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // Update progress bar (0.0 to 1.0)
                    ProgressBar.Value = progress * 100;

                    // Update status message
                    StatusTextBlock.Text = statusMessage;
                }));

                // Log progress milestones to avoid excessive logging
                if (progress == 0 || progress == 0.25 || progress == 0.5 || progress == 0.75 || progress == 1.0)
                {
                    App.LogService?.LogDebug($"[{_operationId}] Progress milestone: {progress:P0}");
                }
            }, $"Update progress in backup window", false);  // Don't show errors to user for progress updates
        }

        public void SetCompleted()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Operation completed successfully");

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

                    App.LogService?.LogDebug($"[{_operationId}] UI updated to completed state");
                }));

                // Log additional operation details if detailed logging is enabled
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService.LogDebug($"[{_operationId}] Completion details:");
                    App.LogService.LogDebug($"  Operation: {_operationType}");
                    App.LogService.LogDebug($"  Completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                }
            }, $"Set completed state in backup window");
        }

        public void ShowError(string errorMessage)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Error($"[{_operationId}] Operation failed: {errorMessage}");

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

                    App.LogService?.LogDebug($"[{_operationId}] UI updated to error state");
                }));

                // Log additional error context if detailed logging is enabled
                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService.LogDebug($"[{_operationId}] Error context:");
                    App.LogService.LogDebug($"  Operation: {_operationType}");
                    App.LogService.LogDebug($"  Error time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    App.LogService.LogDebug($"  Error message: {errorMessage}");
                }
            }, $"Show error in backup window");
        }

        // Command for dragging the window
        public ICommand DragMoveCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"[{_operationId}] Window drag initiated");
                DragMove();
            }, "Window drag operation");
        });

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
        public ICommand CloseCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Closing backup progress window");
                Close();
            }, "Close backup window");
        });
    }
}
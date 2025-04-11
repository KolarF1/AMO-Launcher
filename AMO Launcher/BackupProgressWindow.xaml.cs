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
        private string _operationId;
        private string _operationType;

        public BackupProgressWindow()
        {
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

        public void SetOperationType(string operationType)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Setting operation type: {operationType}");
                _operationType = operationType;

                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
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

                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    ProgressBar.Value = progress * 100;

                    StatusTextBlock.Text = statusMessage;
                }));

                if (progress == 0 || progress == 0.25 || progress == 0.5 || progress == 0.75 || progress == 1.0)
                {
                    App.LogService?.LogDebug($"[{_operationId}] Progress milestone: {progress:P0}");
                }
            }, $"Update progress in backup window", false);
        }

        public void SetCompleted()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{_operationId}] Operation completed successfully");

                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    StatusTextBlock.Text = "Operation completed successfully!";
                    ProgressBar.Value = 100;

                    CloseButton.Content = "Close";
                    CloseButton.IsEnabled = true;

                    CompletedIcon.Visibility = Visibility.Visible;
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.LightGreen);

                    App.LogService?.LogDebug($"[{_operationId}] UI updated to completed state");
                }));

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

                this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    StatusTextBlock.Text = $"Error: {errorMessage}";

                    ProgressBar.Foreground = new SolidColorBrush(Colors.Red);

                    CloseButton.Content = "Close";
                    CloseButton.IsEnabled = true;

                    ErrorIcon.Visibility = Visibility.Visible;
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);

                    App.LogService?.LogDebug($"[{_operationId}] UI updated to error state");
                }));

                if (App.LogService?.ShouldLogDebug() == true)
                {
                    App.LogService.LogDebug($"[{_operationId}] Error context:");
                    App.LogService.LogDebug($"  Operation: {_operationType}");
                    App.LogService.LogDebug($"  Error time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    App.LogService.LogDebug($"  Error message: {errorMessage}");
                }
            }, $"Show error in backup window");
        }

        public ICommand DragMoveCommand => new RelayCommand(() =>
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace($"[{_operationId}] Window drag initiated");
                DragMove();
            }, "Window drag operation");
        });

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
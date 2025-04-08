using System;
using System.Windows;
using System.Windows.Input;
using AMO_Launcher.Models;
using System.Threading.Tasks;
using System.IO;

namespace AMO_Launcher.Views
{
    public partial class SettingsWindow : Window
    {
        private GameInfo _currentGame;

        public SettingsWindow()
        {
            InitializeComponent();

            // Set application version
            string version = GetAppVersion();
            VersionTextBlock.Text = $"Version {version}";

            // Get current game from MainWindow
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                _currentGame = mainWindow.GetCurrentGame();

                // Disable the Reset button if no game is selected
                if (_currentGame == null)
                {
                    ResetGameDataButton.IsEnabled = false;
                    ResetGameDataButton.ToolTip = "No game selected";
                }

                // Disable the button for F1 Manager games (they don't need backup)
                else if (_currentGame.Name != null && _currentGame.Name.Contains("Manager"))
                {
                    ResetGameDataButton.IsEnabled = false;
                    ResetGameDataButton.ToolTip = "F1 Manager games don't require Original Game Data backup";
                }
            }
            else
            {
                ResetGameDataButton.IsEnabled = false;
                ResetGameDataButton.ToolTip = "No game selected";
            }
        }

        // Get application version from assembly
        private string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        #region Custom Title Bar Handlers

        // Handle window dragging
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        // Handle close button click
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region UI Event Handlers

        // Save button click handler
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // In the future, this will save actual settings
            MessageBox.Show("Settings saved successfully.", "Settings Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Reset Game Data button click handler
        private async void ResetGameDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGame == null)
            {
                MessageBox.Show("No game is currently selected.", "Cannot Reset Game Data",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if Original_GameData folder exists
            string gameInstallDir = _currentGame.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");
            bool backupExists = Directory.Exists(backupDir);

            if (!backupExists)
            {
                MessageBox.Show($"No Original_GameData backup found for {_currentGame.Name}. A new backup will be created.",
                    "No Existing Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Show the game verification dialog
            var dialog = new GameVerificationDialog(_currentGame, true);
            dialog.Owner = this;

            // Customize dialog text for reset operation
            dialog.CustomizeForReset();

            bool? result = dialog.ShowDialog();

            if (result == true && dialog.UserConfirmed)
            {
                // User confirmed, perform the reset
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    // Delete existing backup if it exists
                    if (backupExists)
                    {
                        Directory.Delete(backupDir, true);
                    }

                    // Force game version change in GameBackupService to ensure a new backup is created
                    var gameBackupService = App.GameBackupService;

                    // Create a new backup using the existing method
                    bool backupReady = await gameBackupService.EnsureOriginalGameDataBackupAsync(_currentGame);

                    Mouse.OverrideCursor = null;

                    if (backupReady)
                    {
                        MessageBox.Show($"Original game data for {_currentGame.Name} has been successfully reset.",
                            "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to reset original game data for {_currentGame.Name}. The operation was cancelled or failed.",
                            "Reset Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show($"Error resetting original game data: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
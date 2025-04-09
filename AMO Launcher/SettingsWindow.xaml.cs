using System;
using System.Windows;
using System.Windows.Input;
using AMO_Launcher.Models;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Controls;

namespace AMO_Launcher.Views
{
    public partial class SettingsWindow : Window
    {
        private GameInfo _currentGame;
        private bool _settingsChanged = false;

        public SettingsWindow()
        {
            InitializeComponent();

            // Set application version
            string version = GetAppVersion();
            VersionTextBlock.Text = $"Current Version: v{version}";
            AboutVersionTextBlock.Text = $"v{version}";

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

            // Load current settings and initialize controls
            LoadSettings();
        }

        // Load settings from ConfigurationService
        private void LoadSettings()
        {
            try
            {
                var configService = App.ConfigService;

                // Load toggle settings
                AutoDetectGamesCheckBox.IsChecked = configService.GetAutoDetectGamesAtStartup();
                AutoCheckUpdatesCheckBox.IsChecked = configService.GetAutoCheckForUpdatesAtStartup();
                DetailedLoggingCheckBox.IsChecked = configService.GetEnableDetailedLogging();
                LowUsageModeCheckBox.IsChecked = configService.GetLowUsageMode();
                RememberLastGameCheckBox.IsChecked = configService.GetRememberLastSelectedGame();

                // Load launcher action setting
                string launcherAction = configService.GetLauncherActionOnGameLaunch();
                switch (launcherAction)
                {
                    case "None":
                        LauncherActionComboBox.SelectedIndex = 0;
                        break;
                    case "Close":
                        LauncherActionComboBox.SelectedIndex = 2;
                        break;
                    case "Minimize":
                    default:
                        LauncherActionComboBox.SelectedIndex = 1;
                        break;
                }

                // Load custom paths
                var customPaths = configService.GetCustomGameScanPaths();
                CustomPathsListBox.Items.Clear();
                foreach (var path in customPaths)
                {
                    CustomPathsListBox.Items.Add(path);
                }

                // Reset settings changed flag
                _settingsChanged = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Save settings to ConfigurationService
        private async Task SaveSettingsAsync()
        {
            try
            {
                var configService = App.ConfigService;

                // Save toggle settings
                configService.SetAutoDetectGamesAtStartup(AutoDetectGamesCheckBox.IsChecked ?? true);
                configService.SetAutoCheckForUpdatesAtStartup(AutoCheckUpdatesCheckBox.IsChecked ?? true);
                configService.SetEnableDetailedLogging(DetailedLoggingCheckBox.IsChecked ?? false);
                if (App.LogService != null)
                {
                    App.LogService.UpdateDetailedLogging(DetailedLoggingCheckBox.IsChecked ?? false);
                }
                configService.SetLowUsageMode(LowUsageModeCheckBox.IsChecked ?? false);
                configService.SetRememberLastSelectedGame(RememberLastGameCheckBox.IsChecked ?? true);

                // Save launcher action setting
                string launcherAction = "Minimize";
                switch (LauncherActionComboBox.SelectedIndex)
                {
                    case 0:
                        launcherAction = "None";
                        break;
                    case 1:
                        launcherAction = "Minimize";
                        break;
                    case 2:
                        launcherAction = "Close";
                        break;
                }
                configService.SetLauncherActionOnGameLaunch(launcherAction);

                // Save custom paths
                configService.ClearCustomGameScanPaths();
                foreach (var item in CustomPathsListBox.Items)
                {
                    configService.AddCustomGameScanPath(item.ToString());
                }

                // Persist settings to disk
                await configService.SaveSettingsAsync();

                // Reset settings changed flag
                _settingsChanged = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (_settingsChanged)
            {
                var result = MessageBox.Show("You have unsaved changes. Do you want to save them before closing?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveSettings();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }
            Close();
        }

        #endregion

        #region UI Event Handlers

        // Save button click handler
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private async void SaveSettings()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await SaveSettingsAsync();
                _settingsChanged = false;
                Close();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsChanged)
            {
                var result = MessageBox.Show("You have unsaved changes. Are you sure you want to cancel?",
                    "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
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

        // Check for Updates button click handler
        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // Disable the button to prevent multiple clicks
                CheckForUpdatesButton.IsEnabled = false;
                CheckForUpdatesButton.Content = "Checking...";

                // Call the update service to check for updates
                await App.UpdateService.CheckForUpdatesAsync(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}",
                    "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;

                // Re-enable the button
                CheckForUpdatesButton.IsEnabled = true;
                CheckForUpdatesButton.Content = "Check for Updates";
            }
        }

        // Setting checkbox changed handler
        private void SettingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _settingsChanged = true;
        }

        // Launcher action combobox selection changed handler
        private void LauncherActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _settingsChanged = true;
        }

        // Add custom path button click handler
        private void AddPathButton_Click(object sender, RoutedEventArgs e)
        {
            // Use a workaround with OpenFileDialog to select a folder
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select a folder to scan for games",
                CheckFileExists = false,
                FileName = "Select Folder", // This is a hint text
                Filter = "Folders|*.none" // Not actually used since we're selecting folders
            };

            // Get the result
            if (openFileDialog.ShowDialog() == true)
            {
                // Get the directory from the selected file path
                string selectedPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Check if path already exists in the list
                    foreach (var item in CustomPathsListBox.Items)
                    {
                        if (item.ToString().Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("This path is already in the list.", "Duplicate Path",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                    }

                    // Add the path to the list
                    CustomPathsListBox.Items.Add(selectedPath);
                    _settingsChanged = true;
                }
            }
        }

        // Remove custom path button click handler
        private void RemovePathButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomPathsListBox.SelectedItem != null)
            {
                CustomPathsListBox.Items.Remove(CustomPathsListBox.SelectedItem);
                _settingsChanged = true;
            }
            else
            {
                MessageBox.Show("Please select a path to remove.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Clear cache button click handler
        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will clear all cached icons and temporary files. Continue?",
                "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    App.ConfigService.ClearCache();
                    Mouse.OverrideCursor = null;

                    MessageBox.Show("Cache cleared successfully.", "Cache Cleared",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show($"Error clearing cache: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Reset settings button click handler
        private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will reset all launcher settings to their default values. Game data and profiles will not be affected. Continue?",
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await App.ConfigService.ResetToDefaultSettingsAsync();
                    LoadSettings(); // Reload the settings in the UI
                    Mouse.OverrideCursor = null;

                    MessageBox.Show("Settings have been reset to defaults.", "Settings Reset",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show($"Error resetting settings: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Open settings folder button click handler
        private void OpenSettingsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                if (Directory.Exists(appDataPath))
                {
                    Process.Start("explorer.exe", appDataPath);
                }
                else
                {
                    MessageBox.Show("Settings folder does not exist.", "Folder Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening settings folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Discord button click handler
        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/f1game", // Replace with actual invite code
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Discord link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Help button click handler
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/KolarF1/AMO-Launcher/wiki", // Replace with actual help URL
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening help page: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
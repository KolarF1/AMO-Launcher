using System;
using System.Windows;
using System.Windows.Input;
using AMO_Launcher.Models;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Controls;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Views
{
    public partial class SettingsWindow : Window
    {
        private GameInfo _currentGame;
        private bool _settingsChanged = false;
        private string _originalLauncherAction;
        private bool _originalDetailedLogging;

        public SettingsWindow()
        {
            InitializeComponent();

            App.LogService.Info("Opening Settings window");

            ErrorHandler.ExecuteSafe(() =>
            {
                string version = GetAppVersion();
                VersionTextBlock.Text = $"Current Version: v{version}";
                AboutVersionTextBlock.Text = $"v{version}";

                App.LogService.LogDebug($"Application version: {version}");

                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    _currentGame = mainWindow.GetCurrentGame();
                    App.LogService.LogDebug($"Current game: {_currentGame?.Name ?? "None"}");

                    if (_currentGame == null)
                    {
                        ResetGameDataButton.IsEnabled = false;
                        ResetGameDataButton.ToolTip = "No game selected";
                        App.LogService.LogDebug("Reset button disabled: No game selected");
                    }

                    else if (_currentGame.Name != null && _currentGame.Name.Contains("Manager"))
                    {
                        ResetGameDataButton.IsEnabled = false;
                        ResetGameDataButton.ToolTip = "F1 Manager games don't require Original Game Data backup";
                        App.LogService.LogDebug("Reset button disabled: F1 Manager game selected");
                    }
                }
                else
                {
                    ResetGameDataButton.IsEnabled = false;
                    ResetGameDataButton.ToolTip = "No game selected";
                    App.LogService.LogDebug("Reset button disabled: No MainWindow found");
                }

                LoadSettings();
            }, "SettingsWindow initialization");
        }

        private void LoadSettings()
        {
            App.LogService.LogDebug("Loading application settings");

            ErrorHandler.ExecuteSafe(() =>
            {
                var configService = App.ConfigService;

                AutoDetectGamesCheckBox.IsChecked = configService.GetAutoDetectGamesAtStartup();
                AutoCheckUpdatesCheckBox.IsChecked = configService.GetAutoCheckForUpdatesAtStartup();
                DetailedLoggingCheckBox.IsChecked = configService.GetEnableDetailedLogging();
                LowUsageModeCheckBox.IsChecked = configService.GetLowUsageMode();
                RememberLastGameCheckBox.IsChecked = configService.GetRememberLastSelectedGame();

                _originalDetailedLogging = DetailedLoggingCheckBox.IsChecked ?? false;

                string launcherAction = configService.GetLauncherActionOnGameLaunch();
                _originalLauncherAction = launcherAction;

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

                var customPaths = configService.GetCustomGameScanPaths();
                App.LogService.LogDebug($"Loading {customPaths.Count} custom scan paths");

                CustomPathsListBox.Items.Clear();
                foreach (var path in customPaths)
                {
                    CustomPathsListBox.Items.Add(path);
                    App.LogService.Trace($"Added custom path: {path}");
                }

                _settingsChanged = false;

                App.LogService.Info("Settings loaded successfully");
            }, "Loading settings", true);
        }

        private async Task SaveSettingsAsync()
        {
            App.LogService.Info("Saving application settings");

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var configService = App.ConfigService;
                var startTime = DateTime.Now;

                bool detailedLoggingChanged =
                    _originalDetailedLogging != (DetailedLoggingCheckBox.IsChecked ?? false);

                if (detailedLoggingChanged)
                {
                    App.LogService.Info($"Setting changed: Detailed logging {(DetailedLoggingCheckBox.IsChecked == true ? "enabled" : "disabled")}");
                }

                configService.SetAutoDetectGamesAtStartup(AutoDetectGamesCheckBox.IsChecked ?? true);
                configService.SetAutoCheckForUpdatesAtStartup(AutoCheckUpdatesCheckBox.IsChecked ?? true);
                configService.SetEnableDetailedLogging(DetailedLoggingCheckBox.IsChecked ?? false);
                configService.SetLowUsageMode(LowUsageModeCheckBox.IsChecked ?? false);
                configService.SetRememberLastSelectedGame(RememberLastGameCheckBox.IsChecked ?? true);

                if (detailedLoggingChanged && App.LogService != null)
                {
                    App.LogService.LogDebug("Updating LogService detailed logging setting");
                    App.LogService.UpdateDetailedLogging(DetailedLoggingCheckBox.IsChecked ?? false);
                }

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

                if (launcherAction != _originalLauncherAction)
                {
                    App.LogService.Info($"Setting changed: Launcher action on game launch = {launcherAction}");
                }

                configService.SetLauncherActionOnGameLaunch(launcherAction);

                App.LogService.LogDebug($"Saving {CustomPathsListBox.Items.Count} custom scan paths");
                configService.ClearCustomGameScanPaths();
                foreach (var item in CustomPathsListBox.Items)
                {
                    configService.AddCustomGameScanPath(item.ToString());
                    App.LogService.Trace($"Added custom path: {item}");
                }

                App.LogService.LogDebug("Persisting settings to disk");
                await configService.SaveSettingsAsync();

                _settingsChanged = false;

                var elapsed = DateTime.Now - startTime;
                App.LogService.LogDebug($"Settings saved in {elapsed.TotalMilliseconds:F1}ms");

                App.LogService.Info("Settings saved successfully");
            }, "Saving settings", true);
        }

        private string GetAppVersion()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }, "Getting application version", false, "0.0.0");
        }

        #region Custom Title Bar Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                this.DragMove();
            }, "Window dragging", false);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Close button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                if (_settingsChanged)
                {
                    var result = MessageBox.Show("You have unsaved changes. Do you want to save them before closing?",
                        "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.LogService.LogDebug("User chose to save settings before closing");
                        SaveSettings();
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        App.LogService.LogDebug("User cancelled closing operation");
                        return;
                    }
                    else
                    {
                        App.LogService.LogDebug("User chose to discard changes");
                    }
                }
                Close();
            }, "Closing settings window");
        }

        #endregion

        #region UI Event Handlers

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Save button clicked");
            SaveSettings();
        }

        private async void SaveSettings()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
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
            }, "Saving settings on user request");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Cancel button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                if (_settingsChanged)
                {
                    var result = MessageBox.Show("You have unsaved changes. Are you sure you want to cancel?",
                        "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        App.LogService.LogDebug("User cancelled discarding changes");
                        return;
                    }

                    App.LogService.LogDebug("User confirmed discarding changes");
                }
                Close();
            }, "Canceling settings changes");
        }

        private async void ResetGameDataButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.Info("Reset Game Data button clicked");

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (_currentGame == null)
                {
                    App.LogService.Warning("Reset Game Data attempted without a selected game");
                    MessageBox.Show("No game is currently selected.", "Cannot Reset Game Data",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string gameInstallDir = _currentGame.InstallDirectory;
                string backupDir = Path.Combine(gameInstallDir, "Original_GameData");
                bool backupExists = Directory.Exists(backupDir);

                App.LogService.LogDebug($"Checking for existing backup at: {backupDir}");
                App.LogService.LogDebug($"Backup exists: {backupExists}");

                if (!backupExists)
                {
                    MessageBox.Show($"No Original_GameData backup found for {_currentGame.Name}. A new backup will be created.",
                        "No Existing Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                App.LogService.LogDebug("Opening game verification dialog for reset operation");
                var dialog = new GameVerificationDialog(_currentGame, true);
                dialog.Owner = this;

                dialog.CustomizeForReset();

                bool? result = dialog.ShowDialog();

                if (result == true && dialog.UserConfirmed)
                {
                    App.LogService.Info($"User confirmed reset of game data for {_currentGame.Name}");

                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;

                        if (backupExists)
                        {
                            App.LogService.LogDebug($"Deleting existing backup at: {backupDir}");
                            Directory.Delete(backupDir, true);
                        }

                        var gameBackupService = App.GameBackupService;

                        App.LogService.Info($"Creating new backup for {_currentGame.Name}");
                        bool backupReady = await gameBackupService.EnsureOriginalGameDataBackupAsync(_currentGame);

                        Mouse.OverrideCursor = null;

                        if (backupReady)
                        {
                            App.LogService.Info($"Reset completed successfully for {_currentGame.Name}");
                            MessageBox.Show($"Original game data for {_currentGame.Name} has been successfully reset.",
                                "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            App.LogService.Error($"Failed to create backup for {_currentGame.Name}");
                            MessageBox.Show($"Failed to reset original game data for {_currentGame.Name}. The operation was cancelled or failed.",
                                "Reset Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;
                        App.LogService.Error($"Error during game data reset: {ex.Message}");
                        App.LogService.LogDebug($"Reset error details: {ex}");

                        MessageBox.Show($"Error resetting original game data: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    App.LogService.LogDebug("User cancelled game data reset operation");
                }
            }, "Resetting game data");
        }

        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.Info("Check for Updates button clicked");

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var startTime = DateTime.Now;

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    CheckForUpdatesButton.IsEnabled = false;
                    CheckForUpdatesButton.Content = "Checking...";

                    App.LogService.LogDebug("Initiating update check");
                    await App.UpdateService.CheckForUpdatesAsync(false);

                    var elapsed = DateTime.Now - startTime;
                    App.LogService.LogDebug($"Update check completed in {elapsed.TotalMilliseconds:F1}ms");
                }
                finally
                {
                    Mouse.OverrideCursor = null;

                    CheckForUpdatesButton.IsEnabled = true;
                    CheckForUpdatesButton.Content = "Check for Updates";
                }
            }, "Checking for updates", true);
        }

        private void SettingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            if (checkbox != null)
            {
                App.LogService.Trace($"Setting changed: {checkbox.Name} = {checkbox.IsChecked}");
            }

            _settingsChanged = true;
        }

        private void LauncherActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            App.LogService.Trace($"Setting changed: LauncherAction = {LauncherActionComboBox.SelectedIndex}");
            _settingsChanged = true;
        }

        private void AddPathButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Add custom path button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select a folder to scan for games",
                    CheckFileExists = false,
                    FileName = "Select Folder",
                    Filter = "Folders|*.none"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedPath = Path.GetDirectoryName(openFileDialog.FileName);
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        App.LogService.LogDebug($"User selected path: {selectedPath}");

                        foreach (var item in CustomPathsListBox.Items)
                        {
                            if (item.ToString().Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                App.LogService.LogDebug("Selected path is already in the list");
                                MessageBox.Show("This path is already in the list.", "Duplicate Path",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                        }

                        CustomPathsListBox.Items.Add(selectedPath);
                        App.LogService.Info($"Added custom scan path: {selectedPath}");
                        _settingsChanged = true;
                    }
                }
            }, "Adding custom scan path");
        }

        private void RemovePathButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Remove custom path button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                if (CustomPathsListBox.SelectedItem != null)
                {
                    string path = CustomPathsListBox.SelectedItem.ToString();
                    CustomPathsListBox.Items.Remove(CustomPathsListBox.SelectedItem);
                    App.LogService.Info($"Removed custom scan path: {path}");
                    _settingsChanged = true;
                }
                else
                {
                    App.LogService.LogDebug("No path selected for removal");
                    MessageBox.Show("Please select a path to remove.", "No Selection",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, "Removing custom scan path");
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.Info("Clear cache button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                var result = MessageBox.Show(
                    "This will clear all cached icons and temporary files. Continue?",
                    "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    App.LogService.LogDebug("User confirmed cache clearing");

                    try
                    {
                        var startTime = DateTime.Now;
                        Mouse.OverrideCursor = Cursors.Wait;

                        App.ConfigService.ClearCache();

                        var elapsed = DateTime.Now - startTime;
                        App.LogService.LogDebug($"Cache cleared in {elapsed.TotalMilliseconds:F1}ms");

                        Mouse.OverrideCursor = null;

                        App.LogService.Info("Cache cleared successfully");
                        MessageBox.Show("Cache cleared successfully.", "Cache Cleared",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;
                        throw;
                    }
                }
                else
                {
                    App.LogService.LogDebug("User cancelled cache clearing");
                }
            }, "Clearing application cache", true);
        }

        private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.Info("Reset settings button clicked");

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var result = MessageBox.Show(
                    "This will reset all launcher settings to their default values. Game data and profiles will not be affected. Continue?",
                    "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    App.LogService.LogDebug("User confirmed settings reset");

                    try
                    {
                        var startTime = DateTime.Now;
                        Mouse.OverrideCursor = Cursors.Wait;

                        await App.ConfigService.ResetToDefaultSettingsAsync();

                        var elapsed = DateTime.Now - startTime;
                        App.LogService.LogDebug($"Settings reset in {elapsed.TotalMilliseconds:F1}ms");

                        LoadSettings();
                        Mouse.OverrideCursor = null;

                        App.LogService.Info("Settings reset to defaults successfully");
                        MessageBox.Show("Settings have been reset to defaults.", "Settings Reset",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;
                        throw;
                    }
                }
                else
                {
                    App.LogService.LogDebug("User cancelled settings reset");
                }
            }, "Resetting application settings", true);
        }

        private void OpenSettingsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Open settings folder button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                App.LogService.LogDebug($"Settings folder path: {appDataPath}");

                if (Directory.Exists(appDataPath))
                {
                    App.LogService.Info($"Opening settings folder: {appDataPath}");
                    Process.Start("explorer.exe", appDataPath);
                }
                else
                {
                    App.LogService.Warning($"Settings folder not found: {appDataPath}");
                    MessageBox.Show("Settings folder does not exist.", "Folder Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }, "Opening settings folder", true);
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Discord button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                string discordUrl = "https://discord.gg/f1game";
                App.LogService.Info($"Opening Discord link: {discordUrl}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = discordUrl,
                    UseShellExecute = true
                });
            }, "Opening Discord link", true);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            App.LogService.LogDebug("Help button clicked");

            ErrorHandler.ExecuteSafe(() =>
            {
                string helpUrl = "https://github.com/KolarF1/AMO-Launcher/wiki";
                App.LogService.Info($"Opening help page: {helpUrl}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = helpUrl,
                    UseShellExecute = true
                });
            }, "Opening help page", true);
        }

        #endregion
    }
}
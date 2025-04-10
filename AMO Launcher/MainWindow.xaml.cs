using AMO_Launcher.Models;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;
using AMO_Launcher.Views;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;

namespace AMO_Launcher
{
    public partial class MainWindow : Window
    {
        private GameDetectionService _gameDetectionService => App.GameDetectionService;
        private ConfigurationService _configService => App.ConfigService;
        private ModDetectionService _modDetectionService => App.ModDetectionService;
        private GameBackupService _gameBackupService => App.GameBackupService;
        private ProfileService _profileService => App.ProfileService;
        private List<ModProfile> _profiles = new List<ModProfile>();
        private ModProfile _activeProfile;
        private ObservableCollection<object> _selectedTreeViewItems = new ObservableCollection<object>();
        private object _lastSelectedItem = null;
        private bool _isCtrlPressed = false;
        private bool _isShiftPressed = false;
        private List<object> _flattenedTreeItems = new List<object>();

        private ObservableCollection<ModInfo> _availableModsFlat;
        private ObservableCollection<ModCategory> _availableModsCategories;
        private ObservableCollection<ModInfo> _appliedMods;
        private ObservableCollection<ModCategory> _appliedModsCategories;

        private GameInfo _currentGame;

        private bool _appliedModsChanged = false;

        private bool _isMaximized = false;

        // Define error categories for better organization
        private enum ErrorCategory
        {
            FileSystem,     // File and directory access errors
            GameDetection,  // Game detection and selection errors
            ModProcessing,  // Errors during mod processing
            GameExecution,  // Game launch and execution errors
            Configuration,  // Configuration and settings errors
            UI,             // User interface errors
            ProfileManagement, // Profile-related errors
            Unknown         // Uncategorized errors
        }

        public MainWindow()
        {
            App.LogService.Info("Initializing MainWindow");

            try
            {
                InitializeComponent();
                InitializeConflictSystem();

                // Initialize multi-select functionality for the TreeView
                InitializeTreeViewMultiSelect();

                App.UpdateService.UpdateAvailable += UpdateService_UpdateAvailable;
                App.UpdateService.UpdateCheckFailed += UpdateService_UpdateCheckFailed;

                _availableModsFlat = new ObservableCollection<ModInfo>();
                _availableModsCategories = new ObservableCollection<ModCategory>();
                _appliedMods = new ObservableCollection<ModInfo>();
                _appliedModsCategories = new ObservableCollection<ModCategory>();

                AvailableModsTreeView.ItemsSource = _availableModsCategories;
                AppliedModsTreeView.ItemsSource = _appliedModsCategories;

                Title = $"AMO Launcher v{GetAppVersion()}";

                this.SizeChanged += MainWindow_SizeChanged;

                if (ModTabControl != null)
                {
                    ModTabControl.SelectionChanged += TabControl_SelectionChanged;

                    if (ModActionButtons != null)
                    {
                        ModActionButtons.Visibility =
                            ModTabControl.SelectedItem == AppliedModsTab
                                ? Visibility.Visible
                                : Visibility.Collapsed;
                    }
                }

                // Initialize the applied mods TreeView
                InitializeAppliedModsTreeView();

                App.LogService.LogDebug("MainWindow constructor completed successfully");
            }
            catch (Exception ex)
            {
                LogCategorizedError("Error initializing MainWindow", ex, ErrorCategory.UI);
            }
        }

        private void InitializeAppliedModsTreeView()
        {
            AppliedModsTreeView.SelectedItemChanged += AppliedModsTreeView_SelectedItemChanged;
            AppliedModsTreeView.MouseDoubleClick += AppliedModsTreeView_MouseDoubleClick;
        }

        private string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            using (var perfTracker = new PerformanceTracker("WindowLoaded"))
            {
                FlowTracker.StartFlow("MainWindowLoad");

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    App.LogService.Info("Loading MainWindow");

                    FlowTracker.StepFlow("MainWindowLoad", "LoadSettings");
                    await _configService.LoadSettingsAsync();

                    if (_configService.GetAutoDetectGamesAtStartup())
                    {
                        FlowTracker.StepFlow("MainWindowLoad", "AutoDetectGames");
                        App.LogService.Info("Auto-detecting games at startup");
                        await TrySelectGameAsync();
                    }
                    else
                    {
                        string preferredGameId = _configService.GetPreferredGameId();
                        if (!string.IsNullOrEmpty(preferredGameId))
                        {
                            FlowTracker.StepFlow("MainWindowLoad", "LoadPreferredGame");
                            App.LogService.LogDebug($"Loading preferred game with ID: {preferredGameId}");

                            var settings = await _configService.LoadSettingsAsync();
                            var gameSetting = settings.Games.FirstOrDefault(g => g.Id == preferredGameId);
                            if (gameSetting != null)
                            {
                                var gameInfo = new GameInfo
                                {
                                    Id = gameSetting.Id,
                                    Name = gameSetting.Name,
                                    ExecutablePath = gameSetting.ExecutablePath,
                                    InstallDirectory = gameSetting.InstallDirectory,
                                    IsDefault = gameSetting.IsDefault,
                                    Icon = TryExtractIcon(gameSetting.ExecutablePath)
                                };
                                SetCurrentGame(gameInfo);
                            }
                            else
                            {
                                App.LogService.Warning($"Preferred game ID {preferredGameId} not found in settings");
                                ShowNoGameSelectedUI();
                            }
                        }
                        else
                        {
                            App.LogService.Info("No preferred game ID set, showing empty state");
                            ShowNoGameSelectedUI();
                        }
                    }

                    InitializeUi();

                    App.LogService.Info("MainWindow loaded successfully");
                    FlowTracker.StepFlow("MainWindowLoad", "Complete");
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("MainWindowLoad", "Error");
                    LogCategorizedError("Error during window load", ex, ErrorCategory.UI);
                    MessageBox.Show($"Error during startup: {ex.Message}", "Startup Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("MainWindowLoad");
                }
            }
        }

        #region Custom Title Bar Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMaximized)
            {
                this.WindowState = WindowState.Normal;
                MaximizeButton.Content = "\uE922";
                _isMaximized = false;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Content = "\uE923";
                _isMaximized = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                MaximizeButton.Content = "\uE923";
                _isMaximized = true;
            }
            else
            {
                MaximizeButton.Content = "\uE922";
                _isMaximized = false;
            }
        }

        #endregion

        private async Task TrySelectGameAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.Info("Attempting to select game");
                var settings = await _configService.LoadSettingsAsync();
                bool rememberLastSelected = _configService.GetRememberLastSelectedGame();

                string preferredGameId;

                // Determine which game ID to use based on the remember last selected setting
                if (rememberLastSelected)
                {
                    // Use the last selected game if the setting is enabled
                    preferredGameId = _configService.GetLastSelectedGameId();
                    App.LogService.LogDebug($"Using last selected game ID: {preferredGameId}");

                    // Fall back to default game if no last game exists
                    if (string.IsNullOrEmpty(preferredGameId))
                    {
                        preferredGameId = _configService.GetDefaultGameId();
                        App.LogService.LogDebug($"No last game found, falling back to default ID: {preferredGameId}");
                    }
                }
                else
                {
                    // Use only the default game if the setting is disabled
                    preferredGameId = _configService.GetDefaultGameId();
                    App.LogService.LogDebug($"Using default game ID: {preferredGameId}");
                }

                if (settings.Games.Count > 0 && !string.IsNullOrEmpty(preferredGameId))
                {
                    var gameSetting = settings.Games.FirstOrDefault(g => g.Id == preferredGameId);

                    if (gameSetting != null)
                    {
                        App.LogService.Info($"Found preferred game in settings: {gameSetting.Name}");
                        var gameInfo = new GameInfo
                        {
                            Id = gameSetting.Id,
                            Name = gameSetting.Name,
                            ExecutablePath = gameSetting.ExecutablePath,
                            InstallDirectory = gameSetting.InstallDirectory,
                            IsDefault = gameSetting.IsDefault,
                            Icon = TryExtractIcon(gameSetting.ExecutablePath)
                        };
                        SetCurrentGame(gameInfo);
                        return;
                    }
                    else
                    {
                        App.LogService.Warning($"Game with ID {preferredGameId} not found in settings");
                    }
                }

                App.LogService.Info("Scanning for games...");
                using (var perfTracker = new PerformanceTracker("GameDetection", LogLevel.INFO, 10000))
                {
                    var detectedGames = await _gameDetectionService.ScanForGamesAsync();

                    App.LogService.Info($"Scan complete, found {detectedGames.Count} games");

                    if (detectedGames.Count > 0)
                    {
                        if (detectedGames.Count == 1)
                        {
                            App.LogService.Info($"Auto-selecting the only detected game: {detectedGames[0].Name}");
                            SetCurrentGame(detectedGames[0]);
                        }
                        else
                        {
                            App.LogService.Info($"Multiple games found, showing game selection dialog");
                            ShowGameSelectionDialog();
                        }
                    }
                    else
                    {
                        App.LogService.Warning("No games detected");
                        ShowNoGameSelectedUI();
                    }
                }
            }, "Game selection", true);
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // Previous events - should be kept
            AvailableModsTreeView.SelectedItemChanged += AvailableModsTreeView_SelectedItemChanged;
            AvailableModsTreeView.MouseDoubleClick += AvailableModsTreeView_MouseDoubleClick;
        }

        private void AvailableModsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject originalSource = (DependencyObject)e.OriginalSource;
            while (originalSource != null && !(originalSource is TreeViewItem))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (originalSource is TreeViewItem)
            {
                object item = ((TreeViewItem)originalSource).DataContext;

                if (item is ModInfo selectedMod)
                {
                    App.LogService.LogDebug($"Double-clicked on mod: {selectedMod.Name}");

                    // Multi-select processing: if Control/Shift was used for selection and multiple items are selected
                    if (_selectedTreeViewItems.Count > 1 && (_isCtrlPressed || _isShiftPressed))
                    {
                        foreach (var selectedItem in _selectedTreeViewItems)
                        {
                            if (selectedItem is ModInfo mod)
                            {
                                AddModToApplied(mod);
                            }
                        }

                        App.LogService.Info($"Applied {_selectedTreeViewItems.Count} mods via double-click");
                    }
                    else
                    {
                        // Single item processing
                        AddModToApplied(selectedMod);
                    }

                    ModTabControl.SelectedItem = AppliedModsTab;
                }
            }
        }

        private void AvailableModsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ModInfo selectedMod)
            {
                bool modSelected = selectedMod != null;

                DescriptionTab.Visibility = modSelected ? Visibility.Visible : Visibility.Collapsed;

                if (!modSelected && ModTabControl.SelectedItem == DescriptionTab)
                {
                    ModTabControl.SelectedItem = AppliedModsTab;
                }

                if (modSelected)
                {
                    App.LogService.LogDebug($"Selected mod: {selectedMod.Name}");

                    if (string.IsNullOrWhiteSpace(selectedMod.Description))
                    {
                        ModDescriptionTextBlock.Text = "No Description";
                    }
                    else
                    {
                        ModDescriptionTextBlock.Text = selectedMod.Description;
                    }

                    ModNameTextBlock.Text = selectedMod.Name;
                    ModAuthorTextBlock.Text = selectedMod.Author;
                    ModVersionTextBlock.Text = selectedMod.Version;
                    ModPathTextBlock.Text = selectedMod.ModFolderPath ?? selectedMod.ArchiveSource;
                    ModHeaderImage.Source = selectedMod.Icon;
                }
            }
        }

        private void AppliedModsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Handle selection changed in the applied mods TreeView
            if (e.NewValue is ModInfo selectedMod)
            {
                // Handle mod selection - you can add any specific logic here
                App.LogService.LogDebug($"Selected applied mod: {selectedMod.Name}");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.Info("Opening settings window");
                var settingsWindow = new Views.SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }, "Opening settings");
        }

        private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.Info("Change game button clicked");
                ShowGameSelectionDialog();
            }, "Change game");
        }

        private async void ApplyModsButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                if (_currentGame == null)
                {
                    App.LogService.Warning("Attempted to apply mods with no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                bool modsAdded = false;

                try
                {
                    // Check if we have any items in the multi-selection collection
                    if (_selectedTreeViewItems.Count > 0)
                    {
                        App.LogService.Info($"Applying {_selectedTreeViewItems.Count} selected mods/categories");

                        int modsApplied = 0;
                        foreach (var treeViewSelectedItem in _selectedTreeViewItems)
                        {
                            if (treeViewSelectedItem is ModInfo treeViewSelectedMod)
                            {
                                await AddModToAppliedAsync(treeViewSelectedMod);
                                modsApplied++;
                                modsAdded = true;
                            }
                            else if (treeViewSelectedItem is ModCategory treeViewSelectedCategory)
                            {
                                foreach (var mod in treeViewSelectedCategory.Mods)
                                {
                                    await AddModToAppliedAsync(mod);
                                    modsApplied++;
                                    modsAdded = true;
                                }
                            }
                        }

                        if (modsApplied > 0)
                        {
                            ModTabControl.SelectedItem = AppliedModsTab;
                            App.LogService.Info($"Successfully applied {modsApplied} mods");
                        }
                    }
                    else
                    {
                        // If no multi-selection, fall back to standard selection behavior
                        var treeViewItem = AvailableModsTreeView.SelectedItem;
                        if (treeViewItem == null)
                        {
                            App.LogService.Info("No mods selected to apply");
                            MessageBox.Show("Please select one or more mods to apply.", "No Mods Selected",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        if (treeViewItem is ModInfo treeViewMod)
                        {
                            App.LogService.Info($"Applying selected mod: {treeViewMod.Name}");
                            await AddModToAppliedAsync(treeViewMod);
                            ModTabControl.SelectedItem = AppliedModsTab;
                            modsAdded = true;
                        }
                        else if (treeViewItem is ModCategory treeViewCategory)
                        {
                            App.LogService.Info($"Applying all mods in category: {treeViewCategory.Name} ({treeViewCategory.Mods.Count} mods)");
                            foreach (var mod in treeViewCategory.Mods)
                            {
                                await AddModToAppliedAsync(mod);
                                modsAdded = true;
                            }

                            ModTabControl.SelectedItem = AppliedModsTab;
                        }
                    }

                    // Only save applied mods once at the end if any mods were added
                    if (modsAdded)
                    {
                        await SaveAppliedModsAsync();
                    }
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Apply mods");
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                string tabName = "unknown";

                if (ModTabControl.SelectedItem == AppliedModsTab) tabName = "Applied Mods";
                else if (ModTabControl.SelectedItem == DescriptionTab) tabName = "Description";
                else if (ModTabControl.SelectedItem == ConflictsTab) tabName = "Conflicts";

                App.LogService.LogDebug($"Tab changed to: {tabName}");

                if (ModActionButtons != null)
                {
                    ModActionButtons.Visibility =
                        ModTabControl.SelectedItem == AppliedModsTab
                            ? Visibility.Visible
                            : Visibility.Collapsed;

                    ModActionButtons.UpdateLayout();
                }

                if (ModTabControl.SelectedItem == ConflictsTab)
                {
                    bool hasConflicts = false;

                    if (ConflictsListView.ItemsSource is IEnumerable<ConflictItem> conflictItems)
                    {
                        hasConflicts = conflictItems.Any();
                        App.LogService.LogDebug($"Conflict tab shows {conflictItems.Count()} conflicts");
                    }

                    var parent = VisualTreeHelper.GetParent(ConflictsListView) as Grid;
                    if (parent != null)
                    {
                        var existingMessage = parent.Children.OfType<TextBlock>()
                            .FirstOrDefault(tb => tb.Name == "NoConflictsMessage");

                        if (!hasConflicts && existingMessage == null)
                        {
                            var noConflictsMessage = new TextBlock
                            {
                                Name = "NoConflictsMessage",
                                Text = "No conflicts detected between active mods.",
                                Foreground = Brushes.LightGray,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 30, 0, 0),
                                FontSize = 14
                            };

                            Grid.SetColumn(noConflictsMessage, 0);
                            Grid.SetRow(noConflictsMessage, 0);

                            parent.Children.Add(noConflictsMessage);

                            ConflictsListView.Visibility = Visibility.Collapsed;
                            App.LogService.LogDebug("Added 'no conflicts' message to UI");
                        }
                        else if (hasConflicts && existingMessage != null)
                        {
                            parent.Children.Remove(existingMessage);

                            ConflictsListView.Visibility = Visibility.Visible;
                            App.LogService.LogDebug("Removed 'no conflicts' message from UI");
                        }
                    }
                }
            }, "Tab control selection change");
        }

        private void ShowGameSelectionDialog()
        {
            FlowTracker.StartFlow("GameSelection");

            App.LogService.Info("Showing game selection dialog");

            try
            {
                var gameSelectionWindow = new GameSelectionWindow(_gameDetectionService, _configService);
                gameSelectionWindow.Owner = this;

                FlowTracker.StepFlow("GameSelection", "ShowDialog");
                if (gameSelectionWindow.ShowDialog() == true)
                {
                    var selectedGame = gameSelectionWindow.SelectedGame;
                    if (selectedGame != null)
                    {
                        App.LogService.Info($"User selected game: {selectedGame.Name}");
                        FlowTracker.StepFlow("GameSelection", "SetGame");
                        SetCurrentGame(selectedGame);
                    }
                    else
                    {
                        App.LogService.Warning("Game selection dialog returned true but no game was selected");
                    }
                }
                else if (_currentGame == null)
                {
                    App.LogService.Info("Game selection canceled, showing no game UI");
                    FlowTracker.StepFlow("GameSelection", "NoGameSelected");
                    ShowNoGameSelectedUI();
                }
                FlowTracker.StepFlow("GameSelection", "Complete");
            }
            catch (Exception ex)
            {
                FlowTracker.StepFlow("GameSelection", "Error");
                LogCategorizedError("Error in game selection", ex, ErrorCategory.GameDetection);
            }
            finally
            {
                FlowTracker.EndFlow("GameSelection");
            }
        }

        private async void SetCurrentGame(GameInfo game)
        {
            using (var perfTracker = new PerformanceTracker("SetCurrentGame", LogLevel.INFO, 5000))
            {
                FlowTracker.StartFlow("SetCurrentGame");

                try
                {
                    App.LogService.Info($"Setting current game: {game.Name}");
                    App.LogService.LogDebug($"Game details - Path: {game.ExecutablePath}, ID: {game.Id}");

                    _currentGame = game;
                    CurrentGameTextBlock.Text = game.Name;
                    GameContentPanel.Visibility = Visibility.Visible;
                    _appliedModsChanged = false;

                    FlowTracker.StepFlow("SetCurrentGame", "EnsureBackup");

                    if (!game.Name.Contains("Manager"))
                    {
                        App.LogService.LogDebug("Ensuring game backup exists");
                        bool backupReady = await _gameBackupService.EnsureOriginalGameDataBackupAsync(game);
                        if (!backupReady)
                        {
                            App.LogService.Warning($"Game backup could not be created for {game.Name}");
                            MessageBox.Show(
                                "Game backup was not created. Some mod features may be limited or not work correctly.",
                                "Backup Not Created",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            App.LogService.Info("Game backup verified");
                        }
                    }

                    FlowTracker.StepFlow("SetCurrentGame", "ScanMods");
                    await ScanForModsAsync();

                    FlowTracker.StepFlow("SetCurrentGame", "LoadProfiles");
                    InitializeProfiles();

                    FlowTracker.StepFlow("SetCurrentGame", "LoadAppliedMods");
                    await LoadAppliedModsAsync();

                    App.LogService.Info($"Game {game.Name} fully loaded");
                    FlowTracker.StepFlow("SetCurrentGame", "Complete");
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("SetCurrentGame", "Error");
                    LogCategorizedError($"Error setting current game", ex, ErrorCategory.GameDetection);

                    MessageBox.Show(
                        $"Error loading game: {ex.Message}",
                        "Game Loading Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    FlowTracker.EndFlow("SetCurrentGame");
                }
            }
        }

        private async Task ScanForModsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                using (var perfTracker = new PerformanceTracker("ScanForMods", LogLevel.INFO, 10000))
                {
                    if (_currentGame == null)
                    {
                        App.LogService.Warning("Attempted to scan for mods without a selected game");
                        return;
                    }

                    App.LogService.Info($"Scanning for mods for {_currentGame.Name}");
                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        _availableModsFlat.Clear();
                        _availableModsCategories.Clear();

                        AvailableModsTreeView.Visibility = Visibility.Collapsed;

                        App.LogService.LogDebug("Starting mod scan");
                        var mods = await _modDetectionService.ScanForModsAsync(_currentGame);
                        App.LogService.Info($"Found {mods.Count} mods for {_currentGame.Name}");

                        foreach (var mod in mods)
                        {
                            _availableModsFlat.Add(mod);
                        }

                        var categorizedMods = _availableModsFlat.GroupByCategory();

                        App.LogService.LogDebug($"Organized {_availableModsFlat.Count} mods into {categorizedMods.Count} categories");

                        // Log category statistics
                        // Log category statistics in debug mode
                        App.LogService.LogDebug("Category distribution:");
                        foreach (var category in categorizedMods)
                        {
                            App.LogService.LogDebug($"  Category: {category.Name}, Mods: {category.Mods.Count}");
                        }

                        _availableModsCategories = categorizedMods;

                        AvailableModsTreeView.ItemsSource = null;
                        AvailableModsTreeView.ItemsSource = _availableModsCategories;

                        if (_availableModsFlat.Count > 0)
                        {
                            AvailableModsTreeView.Visibility = Visibility.Visible;
                        }

                        await LoadAppliedModsAsync();
                    }
                    catch (Exception ex)
                    {
                        LogCategorizedError("Error scanning for mods", ex, ErrorCategory.ModProcessing);
                        MessageBox.Show($"Error scanning for mods: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
            }, "Scanning for mods");
        }

        private async Task LoadAppliedModsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                using (var perfTracker = new PerformanceTracker("LoadAppliedMods"))
                {
                    FlowTracker.StartFlow("LoadAppliedMods");

                    try
                    {
                        App.LogService.Info("Loading applied mods");

                        _appliedMods.Clear();

                        if (_currentGame == null)
                        {
                            App.LogService.LogDebug("No game selected");
                            AppliedModsTreeView.Visibility = Visibility.Collapsed;
                            FlowTracker.StepFlow("LoadAppliedMods", "NoGame");
                            return;
                        }

                        string normalizedGameId = GetNormalizedCurrentGameId();
                        App.LogService.LogDebug($"Loading mods for normalized game ID: {normalizedGameId}");

                        if (_activeProfile != null && _activeProfile.AppliedMods != null)
                        {
                            App.LogService.LogDebug($"Loading mods from active profile: {_activeProfile.Name} (ID: {_activeProfile.Id})");
                            App.LogService.LogDebug($"Profile has {_activeProfile.AppliedMods.Count} mods");

                            if (_activeProfile.AppliedMods.Count > 0)
                            {
                                FlowTracker.StepFlow("LoadAppliedMods", "FromProfile");
                                LoadAppliedModsFromProfile(_activeProfile);
                                FlowTracker.StepFlow("LoadAppliedMods", "Complete");
                                return;
                            }
                            else
                            {
                                App.LogService.LogDebug("Active profile has no mods, checking config service");
                            }
                        }
                        else
                        {
                            App.LogService.LogDebug("No active profile or profile has no mods list");
                        }

                        FlowTracker.StepFlow("LoadAppliedMods", "FromConfig");
                        var appliedModSettings = _configService.GetAppliedMods(normalizedGameId);

                        if ((appliedModSettings == null || appliedModSettings.Count == 0) && normalizedGameId != _currentGame.Id)
                        {
                            App.LogService.LogDebug($"No mods found with normalized ID, trying original ID: {_currentGame.Id}");
                            appliedModSettings = _configService.GetAppliedMods(_currentGame.Id);
                        }

                        if (appliedModSettings == null || appliedModSettings.Count == 0)
                        {
                            App.LogService.LogDebug("No applied mods found in config service");
                            AppliedModsTreeView.Visibility = Visibility.Collapsed;
                            FlowTracker.StepFlow("LoadAppliedMods", "NoMods");
                            return;
                        }

                        App.LogService.LogDebug($"Found {appliedModSettings.Count} mods in config service");

                        foreach (var setting in appliedModSettings)
                        {
                            try
                            {
                                var mod = _availableModsFlat.FirstOrDefault(m =>
                                    m.ModFolderPath == setting.ModFolderPath ||
                                    (m.IsFromArchive && m.ArchiveSource == setting.ArchiveSource));

                                if (mod != null)
                                {
                                    mod.IsApplied = true;
                                    mod.IsActive = setting.IsActive;
                                    _appliedMods.Add(mod);
                                    App.LogService.LogDebug($"Added mod from config: {mod.Name}");
                                }
                                else if (setting.IsFromArchive && !string.IsNullOrEmpty(setting.ArchiveSource))
                                {
                                    App.LogService.LogDebug($"Trying to load archive mod: {setting.ArchiveSource}");
                                    var archiveMod = await _modDetectionService.LoadModFromArchivePathAsync(
                                        setting.ArchiveSource, _currentGame.Name, setting.ArchiveRootPath);

                                    if (archiveMod != null)
                                    {
                                        archiveMod.IsApplied = true;
                                        archiveMod.IsActive = setting.IsActive;
                                        _appliedMods.Add(archiveMod);
                                        App.LogService.LogDebug($"Added archive mod: {archiveMod.Name}");
                                    }
                                    else
                                    {
                                        App.LogService.Warning($"Failed to load archive mod from {setting.ArchiveSource}");
                                    }
                                }
                                else if (!string.IsNullOrEmpty(setting.ModFolderPath))
                                {
                                    App.LogService.LogDebug($"Trying to load folder mod: {setting.ModFolderPath}");
                                    var folderMod = _modDetectionService.LoadModFromFolderPath(setting.ModFolderPath, _currentGame.Name);

                                    if (folderMod != null)
                                    {
                                        folderMod.IsApplied = true;
                                        folderMod.IsActive = setting.IsActive;
                                        _appliedMods.Add(folderMod);
                                        App.LogService.LogDebug($"Added folder mod: {folderMod.Name}");
                                    }
                                    else
                                    {
                                        App.LogService.Warning($"Failed to load folder mod from {setting.ModFolderPath}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogService.Error($"Error adding mod: {ex.Message}");
                                App.LogService.LogDebug($"Stack trace: {ex.StackTrace}");
                            }
                        }

                        // Update the TreeView with the loaded mods
                        UpdateAppliedModsTreeView();

                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        DetectModConflicts();

                        if (_activeProfile != null && _appliedMods.Count > 0)
                        {
                            App.LogService.LogDebug("Saving applied mods to active profile for future use");
                            await SaveAppliedModsAsync();
                        }

                        App.LogService.Info($"Loaded {_appliedMods.Count} applied mods");
                        FlowTracker.StepFlow("LoadAppliedMods", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("LoadAppliedMods", "Error");
                        LogCategorizedError("Error loading applied mods", ex, ErrorCategory.ModProcessing);
                        MessageBox.Show($"Error loading applied mods: {ex.Message}", "Load Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        FlowTracker.EndFlow("LoadAppliedMods");
                    }
                }
            }, "Loading applied mods");
        }

        private void UpdateAppliedModsTreeView()
        {
            if (_appliedMods.Count == 0)
            {
                AppliedModsTreeView.Visibility = Visibility.Collapsed;
                return;
            }

            // Group mods by category - using the same grouping logic as available mods
            _appliedModsCategories.Clear();
            var categorizedMods = _appliedMods.GroupByCategory();

            foreach (var category in categorizedMods)
            {
                _appliedModsCategories.Add(category);
            }

            AppliedModsTreeView.Visibility = Visibility.Visible;
        }

        private async void RefreshModsButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                App.LogService.Info("Refreshing mods");
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    await ScanForModsAsync();
                    App.LogService.Info("Mods refreshed successfully");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Refreshing mods");
        }

        private BitmapImage TryExtractIcon(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService.LogDebug($"Cannot extract icon - invalid path: {executablePath}");
                    return null;
                }

                App.LogService.LogDebug($"Extracting icon from: {executablePath}");

                try
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                    if (icon == null)
                        return null;

                    using (var ms = new MemoryStream())
                    {
                        using (var bitmap = icon.ToBitmap())
                        {
                            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;

                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.StreamSource = ms;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();

                            App.LogService.LogDebug("Icon extracted successfully");
                            return bitmapImage;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LogService.Warning($"Error extracting icon: {ex.Message}");
                    return null;
                }
            }, "Extracting icon", false);
        }

        private void ShowNoGameSelectedUI()
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.Info("Showing no game selected UI");

                _currentGame = null;
                CurrentGameTextBlock.Text = "No game selected";
                GameContentPanel.Visibility = Visibility.Visible;

                _availableModsFlat.Clear();
                AvailableModsTreeView.Visibility = Visibility.Collapsed;

                _appliedMods.Clear();
                AppliedModsTreeView.Visibility = Visibility.Collapsed;
            }, "Show no game UI");
        }

        private async Task AddModToAppliedAsync(ModInfo mod)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                if (mod == null) return;

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    App.LogService.Info($"Adding mod to applied list: {mod.Name}");

                    string modPath = mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath;
                    string absoluteModPath = PathUtility.ToAbsolutePath(modPath);

                    bool alreadyExists = _appliedMods.Any(m =>
                        PathUtility.ToAbsolutePath(m.ModFolderPath) == absoluteModPath ||
                        (m.IsFromArchive && PathUtility.ToAbsolutePath(m.ArchiveSource) == absoluteModPath));

                    if (!alreadyExists)
                    {
                        App.LogService.LogDebug($"Mod not in applied list, adding it");

                        mod.IsApplied = true;
                        mod.IsActive = true;

                        _appliedMods.Add(mod);
                        UpdateAppliedModsTreeView();
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();

                        _configService.MarkModsChanged();

                        App.LogService.LogDebug($"Original mod path: {modPath}");
                        App.LogService.LogDebug($"Relative path: {PathUtility.ToRelativePath(modPath)}");

                        // Note: We don't save here - we'll save once at the end
                        DetectModConflicts();

                        App.LogService.Info($"Mod '{mod.Name}' added to applied list");
                    }
                    else
                    {
                        App.LogService.Info($"Mod '{mod.Name}' already in applied list, skipping");
                    }
                }
                catch (Exception ex)
                {
                    LogCategorizedError($"Error adding mod to applied list", ex, ErrorCategory.ModProcessing);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Add mod to applied list");
        }

        private void AddModToApplied(ModInfo mod)
        {
            ErrorHandler.ExecuteSafe(() => {
                if (mod == null) return;

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    App.LogService.Info($"Adding mod to applied list: {mod.Name}");

                    string modPath = mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath;
                    string absoluteModPath = PathUtility.ToAbsolutePath(modPath);

                    bool alreadyExists = _appliedMods.Any(m =>
                        PathUtility.ToAbsolutePath(m.ModFolderPath) == absoluteModPath ||
                        (m.IsFromArchive && PathUtility.ToAbsolutePath(m.ArchiveSource) == absoluteModPath));

                    if (!alreadyExists)
                    {
                        App.LogService.LogDebug($"Mod not in applied list, adding it");

                        mod.IsApplied = true;
                        mod.IsActive = true;

                        _appliedMods.Add(mod);
                        UpdateAppliedModsTreeView();
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();

                        _configService.MarkModsChanged();

                        App.LogService.LogDebug($"Original mod path: {modPath}");
                        App.LogService.LogDebug($"Relative path: {PathUtility.ToRelativePath(modPath)}");

                        SaveAppliedMods();
                        DetectModConflicts();

                        App.LogService.Info($"Mod '{mod.Name}' added to applied list");
                    }
                    else
                    {
                        App.LogService.Info($"Mod '{mod.Name}' already in applied list, skipping");
                    }
                }
                catch (Exception ex)
                {
                    LogCategorizedError($"Error adding mod to applied list", ex, ErrorCategory.ModProcessing);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Add mod to applied list");
        }

        private void ActiveCheckBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                var checkbox = sender as CheckBox;
                if (checkbox?.DataContext is ModInfo mod)
                {
                    App.LogService.LogDebug($"Changed active state for mod '{mod.Name}' to: {mod.IsActive}");
                }

                _configService.MarkModsChanged();
                SaveAppliedMods();
                DetectModConflicts();
            }, "Toggle mod active state");
        }

        private void ModActionButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                var button = sender as Button;
                if (button != null)
                {
                    string action = button.Tag?.ToString() ?? "";
                    App.LogService.LogDebug($"Mod action button clicked: {action}");

                    switch (action)
                    {
                        case "Remove":
                            RemoveSelectedAppliedMods();
                            break;

                        case "MoveTop":
                            MoveSelectedModToTop();
                            break;

                        case "MoveUp":
                            MoveSelectedModUp();
                            break;

                        case "MoveDown":
                            MoveSelectedModDown();
                            break;

                        case "MoveBottom":
                            MoveSelectedModToBottom();
                            break;

                        default:
                            App.LogService.Warning($"Unknown mod action: {action}");
                            break;
                    }
                }
            }, "Mod action button");
        }

        private void RemoveSelectedAppliedMods()
        {
            ErrorHandler.ExecuteSafe(() => {
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    // Find selected mods in the TreeView
                    ModInfo selectedMod = null;
                    if (AppliedModsTreeView.SelectedItem is ModInfo mod)
                    {
                        selectedMod = mod;
                    }

                    if (selectedMod == null)
                    {
                        App.LogService.Info("No mods selected for removal");
                        MessageBox.Show("Please select a mod to remove.", "No Mods Selected",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    App.LogService.Info($"Removing mod from applied list: {selectedMod.Name}");

                    selectedMod.IsApplied = false;
                    _appliedMods.Remove(selectedMod);

                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    UpdateAppliedModsTreeView();

                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    DetectModConflicts();

                    App.LogService.Info($"Successfully removed mod: {selectedMod.Name}");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Remove selected mods");
        }

        private void MoveSelectedModToTop()
        {
            ErrorHandler.ExecuteSafe(() => {
                // Find the selected mod
                ModInfo selectedMod = null;

                // Check if a mod is selected in the TreeView
                if (AppliedModsTreeView.SelectedItem is ModInfo mod)
                {
                    selectedMod = mod;
                }

                if (selectedMod != null)
                {
                    App.LogService.LogDebug($"Moving mod to top: {selectedMod.Name}");

                    int currentIndex = _appliedMods.IndexOf(selectedMod);
                    if (currentIndex > 0)
                    {
                        _appliedMods.Move(currentIndex, 0);
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();

                        // Reselect the moved item in the TreeView
                        SelectModInTreeView(selectedMod);

                        _configService.MarkModsChanged();
                        SaveAppliedMods();
                        DetectModConflicts();

                        App.LogService.LogDebug($"Moved mod '{selectedMod.Name}' from position {currentIndex} to 0");
                    }
                }
            }, "Move mod to top");
        }

        private void MoveSelectedModUp()
        {
            ErrorHandler.ExecuteSafe(() => {
                // Find the selected mod
                ModInfo selectedMod = null;

                // Check if a mod is selected in the TreeView
                if (AppliedModsTreeView.SelectedItem is ModInfo mod)
                {
                    selectedMod = mod;
                }

                if (selectedMod != null)
                {
                    App.LogService.LogDebug($"Moving mod up: {selectedMod.Name}");

                    int currentIndex = _appliedMods.IndexOf(selectedMod);
                    if (currentIndex > 0)
                    {
                        _appliedMods.Move(currentIndex, currentIndex - 1);
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();

                        // Reselect the moved item in the TreeView
                        SelectModInTreeView(selectedMod);

                        _configService.MarkModsChanged();
                        SaveAppliedMods();
                        DetectModConflicts();

                        App.LogService.LogDebug($"Moved mod '{selectedMod.Name}' from position {currentIndex} to {currentIndex - 1}");
                    }
                }
            }, "Move mod up");
        }

        private void MoveSelectedModDown()
        {
            ErrorHandler.ExecuteSafe(() => {
                // Find the selected mod
                ModInfo selectedMod = null;

                // Check if a mod is selected in the TreeView
                if (AppliedModsTreeView.SelectedItem is ModInfo mod)
                {
                    selectedMod = mod;
                }

                if (selectedMod != null)
                {
                    App.LogService.LogDebug($"Moving mod down: {selectedMod.Name}");

                    int currentIndex = _appliedMods.IndexOf(selectedMod);
                    if (currentIndex < _appliedMods.Count - 1)
                    {
                        _appliedMods.Move(currentIndex, currentIndex + 1);
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();

                        // Reselect the moved item in the TreeView
                        SelectModInTreeView(selectedMod);

                        _configService.MarkModsChanged();
                        SaveAppliedMods();
                        DetectModConflicts();

                        App.LogService.LogDebug($"Moved mod '{selectedMod.Name}' from position {currentIndex} to {currentIndex + 1}");
                    }
                }
            }, "Move mod down");
        }

        private void MoveSelectedModToBottom()
        {
            ErrorHandler.ExecuteSafe(() => {
                // Find the selected mod
                ModInfo selectedMod = null;

                // Check if a mod is selected in the TreeView
                if (AppliedModsTreeView.SelectedItem is ModInfo mod)
                {
                    selectedMod = mod;
                }

                if (selectedMod != null)
                {
                    App.LogService.LogDebug($"Moving mod to bottom: {selectedMod.Name}");

                    int currentIndex = _appliedMods.IndexOf(selectedMod);
                    int lastIndex = _appliedMods.Count - 1;

                    if (currentIndex < lastIndex)
                    {
                        _appliedMods.Move(currentIndex, lastIndex);
                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();

                        // Reselect the moved item in the TreeView
                        SelectModInTreeView(selectedMod);

                        _configService.MarkModsChanged();
                        SaveAppliedMods();
                        DetectModConflicts();

                        App.LogService.LogDebug($"Moved mod '{selectedMod.Name}' from position {currentIndex} to {lastIndex}");
                    }
                }
            }, "Move mod to bottom");
        }

        // Helper method to select a mod in the TreeView
        private void SelectModInTreeView(ModInfo modToSelect)
        {
            foreach (var category in _appliedModsCategories)
            {
                foreach (var mod in category.Mods)
                {
                    if (mod == modToSelect)
                    {
                        // Expand the category
                        var categoryContainer = GetTreeViewItem(AppliedModsTreeView, category);
                        if (categoryContainer != null)
                        {
                            categoryContainer.IsExpanded = true;
                        }

                        // Select the mod
                        var modContainer = GetTreeViewItemRecursive(AppliedModsTreeView, mod);
                        if (modContainer != null)
                        {
                            modContainer.IsSelected = true;
                        }

                        return;
                    }
                }
            }
        }

        // Helper method to find a TreeViewItem by its data context
        private TreeViewItem GetTreeViewItem(ItemsControl container, object item)
        {
            if (container == null) return null;

            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
            {
                return tvi;
            }

            return null;
        }

        // Helper method to find a TreeViewItem recursively
        private TreeViewItem GetTreeViewItemRecursive(ItemsControl container, object item)
        {
            if (container == null) return null;

            // Try to find the item at this level
            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
            {
                return tvi;
            }

            // Search through all items at this level
            for (int i = 0; i < container.Items.Count; i++)
            {
                TreeViewItem childContainer = container.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (childContainer != null)
                {
                    // Search in this container's children
                    TreeViewItem result = GetTreeViewItemRecursive(childContainer, item);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        private async Task SaveAppliedModsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                using (var perfTracker = new PerformanceTracker("SaveAppliedMods"))
                {
                    if (_currentGame == null)
                    {
                        App.LogService.Warning("No game selected, cannot save mods");
                        return;
                    }

                    var appliedModSettings = _appliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.IsFromArchive ? null : PathUtility.ToRelativePath(m.ModFolderPath),
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.IsFromArchive ? PathUtility.ToRelativePath(m.ArchiveSource) : null,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList();

                    App.LogService.LogDebug($"Saving {appliedModSettings.Count} mods for game {_currentGame.Name}");

                    if (App.LogService.ShouldLogTrace())
                    {
                        foreach (var mod in appliedModSettings)
                        {
                            App.LogService.Trace($"  Saving mod path: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}");
                        }
                    }

                    string normalizedGameId = GetNormalizedCurrentGameId();

                    await _configService.SaveAppliedModsAsync(normalizedGameId, appliedModSettings);

                    if (normalizedGameId != _currentGame.Id)
                    {
                        await _configService.SaveAppliedModsAsync(_currentGame.Id, appliedModSettings);
                    }

                    if (_activeProfile != null)
                    {
                        _activeProfile.AppliedMods = new List<AppliedModSetting>(appliedModSettings);
                        _activeProfile.LastModified = DateTime.Now;
                        App.LogService.LogDebug($"Updated active profile '{_activeProfile.Name}' with {appliedModSettings.Count} mods");
                    }

                    await _profileService.UpdateActiveProfileModsAsync(normalizedGameId, appliedModSettings);

                    App.LogService.Info($"Successfully saved {appliedModSettings.Count} mods");
                }
            }, "Saving applied mods");
        }

        private void SaveAppliedMods()
        {
            Task.Run(async () => await SaveAppliedModsAsync()).ConfigureAwait(false);
        }

        private async void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("GameLaunch");

                App.LogService.Info("Game launch initiated");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Attempted to launch game but no game is selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("GameLaunch", "NoGameSelected");
                    return;
                }

                try
                {
                    FlowTracker.StepFlow("GameLaunch", "Preparing");
                    Mouse.OverrideCursor = Cursors.Wait;

                    App.LogService.Info($"Launching game: {_currentGame.Name}");
                    App.LogService.LogDebug($"Game path: {_currentGame.ExecutablePath}");

                    using (var perfTracker = new PerformanceTracker("GameLaunchPreparation", LogLevel.INFO, 5000))
                    {
                        await CheckAndApplyModsAsync();
                    }

                    if (!File.Exists(_currentGame.ExecutablePath))
                    {
                        throw new FileNotFoundException($"Game executable not found: {_currentGame.ExecutablePath}");
                    }

                    App.LogService.LogDebug($"Starting process: {_currentGame.ExecutablePath}");

                    FlowTracker.StepFlow("GameLaunch", "StartProcess");
                    Process.Start(_currentGame.ExecutablePath);

                    _configService.ResetModsChangedFlag();

                    // Handle launcher action based on settings
                    string launcherAction = _configService.GetLauncherActionOnGameLaunch();
                    App.LogService.LogDebug($"Launcher action after game launch: {launcherAction}");

                    FlowTracker.StepFlow("GameLaunch", "PostLaunchAction");
                    switch (launcherAction)
                    {
                        case "Close":
                            App.LogService.Info("Closing launcher after game launch as per settings");
                            Application.Current.Shutdown();
                            break;
                        case "Minimize":
                            App.LogService.LogDebug("Minimizing launcher after game launch as per settings");
                            this.WindowState = WindowState.Minimized;
                            break;
                        case "None":
                        default:
                            App.LogService.LogDebug("Keeping launcher window state unchanged");
                            break;
                    }

                    App.LogService.Info("Game launched successfully");
                    FlowTracker.StepFlow("GameLaunch", "Complete");
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("GameLaunch", "Error");
                    LogCategorizedError("Error launching game", ex, ErrorCategory.GameExecution);
                    MessageBox.Show($"Error launching game: {ex.Message}", "Launch Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("GameLaunch");
                }
            }, "Launch game");
        }

        private async Task CheckAndApplyModsAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                FlowTracker.StartFlow("ApplyModsForLaunch");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot apply mods - no game selected");
                    return;
                }

                try
                {
                    App.LogService.Info($"Checking if mods need to be applied for {_currentGame.Name}");

                    var currentActiveMods = _appliedMods.Where(m => m.IsActive)
                        .Select(m => new AppliedModSetting
                        {
                            ModFolderPath = m.ModFolderPath,
                            IsActive = true,
                            IsFromArchive = m.IsFromArchive,
                            ArchiveSource = m.ArchiveSource,
                            ArchiveRootPath = m.ArchiveRootPath
                        })
                        .ToList();

                    bool modsChanged = _configService.HaveModsChanged(_currentGame.Id, currentActiveMods);

                    if (!modsChanged)
                    {
                        App.LogService.Info("No changes detected in mods - launching game without file operations");
                        FlowTracker.StepFlow("ApplyModsForLaunch", "NoChanges");
                        return;
                    }

                    FlowTracker.StepFlow("ApplyModsForLaunch", "ShowProgress");
                    var progressWindow = new Views.BackupProgressWindow();
                    progressWindow.Owner = this;
                    progressWindow.SetGame(_currentGame);
                    progressWindow.SetOperationType("Preparing Game Launch");
                    Application.Current.Dispatcher.Invoke(() => { progressWindow.Show(); });

                    try
                    {
                        bool hasActiveMods = currentActiveMods.Count > 0;

                        progressWindow.UpdateProgress(0, hasActiveMods ?
                            "Preparing to apply mods..." :
                            "Restoring original game files...");

                        bool isF1ManagerGame = _currentGame.Name.Contains("Manager");

                        if (isF1ManagerGame)
                        {
                            FlowTracker.StepFlow("ApplyModsForLaunch", "F1ManagerMods");
                            progressWindow.UpdateProgress(0.2, hasActiveMods ?
                                "Preparing F1 Manager mods..." :
                                "Removing F1 Manager mods...");

                            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

                            try
                            {
                                App.LogService.Info("Applying F1 Manager mods");
                                await ApplyF1ManagerModsAsync(_currentGame, activeMods);

                                progressWindow.UpdateProgress(0.9, hasActiveMods ?
                                    "All F1 Manager mods applied successfully!" :
                                    "All F1 Manager mods removed successfully!");

                                App.LogService.Info(hasActiveMods ?
                                    $"Applied {activeMods.Count} F1 Manager mods successfully" :
                                    "Removed all F1 Manager mods successfully");
                            }
                            catch (DirectoryNotFoundException ex)
                            {
                                LogCategorizedError("Error applying F1 Manager mods - directory not found", ex, ErrorCategory.FileSystem);
                                progressWindow.ShowError($"Error applying F1 Manager mods: {ex.Message}\n\nPlease check if the game is installed correctly.");
                                await Task.Delay(3000);
                                return;
                            }
                        }
                        else
                        {
                            // Standard game mod application process
                            string gameInstallDir = _currentGame.InstallDirectory;
                            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

                            if (!Directory.Exists(backupDir))
                            {
                                App.LogService.Error($"Original game backup not found at {backupDir}");
                                progressWindow.ShowError("Original game data backup not found. Please reset your game data from the Settings window.");
                                await Task.Delay(3000);
                                return;
                            }

                            FlowTracker.StepFlow("ApplyModsForLaunch", "RestoreBackup");
                            await Task.Run(() =>
                            {
                                progressWindow.UpdateProgress(0.2, "Restoring original game files...");
                                App.LogService.Info($"Restoring game files from backup at {backupDir}");
                                using (var perfTracker = new PerformanceTracker("RestoreGameFiles"))
                                {
                                    CopyDirectoryContents(backupDir, gameInstallDir);
                                }
                            });

                            if (!hasActiveMods)
                            {
                                progressWindow.UpdateProgress(0.9, "Game restored to original state and ready to launch!");
                                App.LogService.Info("Game restored to original state (no active mods)");
                            }
                            else
                            {
                                FlowTracker.StepFlow("ApplyModsForLaunch", "ApplyMods");
                                var activeMods = _appliedMods.Where(m => m.IsActive).ToList();
                                App.LogService.Info($"Applying {activeMods.Count} mods to game");

                                for (int i = 0; i < activeMods.Count; i++)
                                {
                                    var mod = activeMods[i];
                                    double progress = 0.2 + (i * 0.7 / activeMods.Count);

                                    progressWindow.UpdateProgress(progress, $"Applying mod ({i + 1}/{activeMods.Count}): {mod.Name}");
                                    App.LogService.LogDebug($"Applying mod {i + 1}/{activeMods.Count}: {mod.Name}");

                                    await Task.Run(() =>
                                    {
                                        if (mod.IsFromArchive)
                                        {
                                            App.LogService.LogDebug($"Applying archive mod: {mod.ArchiveSource}");
                                            ApplyModFromArchive(mod, gameInstallDir);
                                        }
                                        else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                                        {
                                            App.LogService.LogDebug($"Applying folder mod: {mod.ModFilesPath}");
                                            CopyDirectoryContents(mod.ModFilesPath, gameInstallDir);
                                        }
                                        else
                                        {
                                            App.LogService.Warning($"Cannot apply mod {mod.Name} - invalid path");
                                        }
                                    });
                                }

                                progressWindow.UpdateProgress(0.95, "All mods applied successfully!");
                                App.LogService.Info($"Successfully applied {activeMods.Count} mods");
                            }
                        }

                        FlowTracker.StepFlow("ApplyModsForLaunch", "SaveState");
                        _configService.SaveLastAppliedModsState(_currentGame.Id, currentActiveMods);
                        App.LogService.LogDebug($"Saved last applied mods state for game {_currentGame.Id}");

                        _configService.ResetModsChangedFlag();

                        progressWindow.UpdateProgress(1.0, "Ready to launch game!");
                        await Task.Delay(1000);

                        App.LogService.Info("Game files prepared successfully for launch");
                        FlowTracker.StepFlow("ApplyModsForLaunch", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("ApplyModsForLaunch", "Error");
                        LogCategorizedError("Error preparing game files", ex, ErrorCategory.FileSystem);
                        progressWindow.ShowError($"Error preparing game launch: {ex.Message}");
                        await Task.Delay(3000);
                        throw;
                    }
                    finally
                    {
                        Application.Current.Dispatcher.Invoke(() => { progressWindow.Close(); });
                    }
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error applying mods for game launch", ex, ErrorCategory.ModProcessing);
                    MessageBox.Show($"Error preparing game launch: {ex.Message}", "Launch Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    FlowTracker.EndFlow("ApplyModsForLaunch");
                }
            }, "Checking and applying mods");
        }

        private void UpdateModPriorities()
        {
            ErrorHandler.ExecuteSafe(() => {
                int count = _appliedMods.Count;
                for (int i = 0; i < count; i++)
                {
                    _appliedMods[i].Priority = count - i;
                }
                // Don't need to refresh the ItemsSource as we're using TreeView
                UpdateAppliedModsTreeView();
                App.LogService.LogDebug($"Updated priorities for {count} mods");
            }, "Update mod priorities");
        }

        private void CopyDirectoryContents(string sourceDir, string targetDir, Views.BackupProgressWindow progressWindow = null)
        {
            ErrorHandler.ExecuteSafe(() => {
                try
                {
                    App.LogService.LogDebug($"Copying directory contents from {sourceDir} to {targetDir}");

                    if (!Directory.Exists(sourceDir))
                    {
                        App.LogService.Error($"Source directory not found: {sourceDir}");
                        throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
                    }

                    Directory.CreateDirectory(targetDir);

                    // Count the total number of files for progress tracking
                    if (progressWindow != null)
                    {
                        int totalFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).Length;
                        App.LogService.LogDebug($"Found {totalFiles} files to copy");
                    }

                    int copiedFiles = 0;
                    int errorCount = 0;

                    foreach (string file in Directory.GetFiles(sourceDir))
                    {
                        try
                        {
                            string fileName = Path.GetFileName(file);
                            string destFile = Path.Combine(targetDir, fileName);

                            App.LogService.Trace($"Copying file: {fileName}");
                            File.Copy(file, destFile, true);
                            copiedFiles++;

                            // Update progress periodically to avoid UI lag
                            if (progressWindow != null && copiedFiles % 50 == 0)
                            {
                                progressWindow.UpdateProgress(-1, $"Copied {copiedFiles} files so far...");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            App.LogService.Error($"Error copying file {file}: {ex.Message}");
                        }
                    }

                    foreach (string directory in Directory.GetDirectories(sourceDir))
                    {
                        try
                        {
                            string dirName = Path.GetFileName(directory);
                            string destDir = Path.Combine(targetDir, dirName);

                            App.LogService.Trace($"Processing subdirectory: {dirName}");
                            CopyDirectoryContents(directory, destDir, progressWindow);
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            App.LogService.Error($"Error processing directory {directory}: {ex.Message}");
                        }
                    }

                    App.LogService.LogDebug($"Directory copy complete - copied {copiedFiles} files with {errorCount} errors");
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error copying directory contents", ex, ErrorCategory.FileSystem);
                    throw new IOException($"Failed to copy directory contents: {ex.Message}", ex);
                }
            }, "Copy directory contents", false);
        }

        private void ApplyModFromArchive(ModInfo mod, string targetDir)
        {
            ErrorHandler.ExecuteSafe(() => {
                if (string.IsNullOrEmpty(mod.ArchiveSource) || !File.Exists(mod.ArchiveSource))
                {
                    App.LogService.Warning($"Cannot apply mod from archive - invalid archive path: {mod.ArchiveSource}");
                    return;
                }

                App.LogService.LogDebug($"Applying mod from archive: {mod.ArchiveSource}");
                using (var perfTracker = new PerformanceTracker("ApplyArchiveMod"))
                {
                    using (var archive = SharpCompress.Archives.ArchiveFactory.Open(mod.ArchiveSource))
                    {
                        string modPath = mod.ArchiveRootPath;
                        if (!string.IsNullOrEmpty(modPath))
                        {
                            modPath = Path.Combine(modPath, "Mod");
                            modPath = modPath.Replace('\\', '/');
                        }
                        else
                        {
                            modPath = "Mod";
                        }

                        App.LogService.LogDebug($"Using mod path in archive: {modPath}");
                        int extractedFiles = 0;
                        int totalFiles = archive.Entries.Count(e => !e.IsDirectory);

                        foreach (var entry in archive.Entries)
                        {
                            if (entry.IsDirectory) continue;

                            string entryKey = entry.Key.Replace('\\', '/');

                            App.LogService.Trace($"Processing archive entry: {entryKey}");

                            if (entryKey.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                string relativePath = entryKey.Substring(modPath.Length + 1);
                                string targetPath = Path.Combine(targetDir, relativePath);

                                App.LogService.Trace($"  Extracting to: {relativePath}");

                                string targetDirPath = Path.GetDirectoryName(targetPath);
                                if (!Directory.Exists(targetDirPath))
                                {
                                    Directory.CreateDirectory(targetDirPath);
                                }

                                using (var entryStream = entry.OpenEntryStream())
                                using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                                {
                                    entryStream.CopyTo(fileStream);
                                }

                                extractedFiles++;
                            }
                        }

                        App.LogService.LogDebug($"Extracted {extractedFiles} files out of {totalFiles} total files in archive");
                    }
                }
            }, $"Apply mod from archive: {mod.Name}", false);
        }

        public class RelayCommand<T> : ICommand
        {
            private readonly Action<T> _execute;
            private readonly Predicate<T> _canExecute;

            public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object parameter)
            {
                return _canExecute == null || _canExecute((T)parameter);
            }

            public void Execute(object parameter)
            {
                _execute((T)parameter);
            }

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }

        public GameInfo GetCurrentGame()
        {
            return _currentGame;
        }

        private void UpdateModPriorityDisplays()
        {
            ErrorHandler.ExecuteSafe(() => {
                int count = _appliedMods.Count;
                for (int i = 0; i < count; i++)
                {
                    _appliedMods[i].PriorityDisplay = (count - i).ToString();
                }
                App.LogService.LogDebug($"Updated priority displays for {count} mods");
            }, "Update mod priority displays");
        }

        private void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                var menuItem = sender as MenuItem;
                if (menuItem == null) return;

                var contextMenu = menuItem.Parent as ContextMenu;
                if (contextMenu == null) return;

                var targetControl = contextMenu.PlacementTarget;
                object selectedItem = null;

                // Determine which control triggered the context menu
                if (targetControl is TreeView treeView)
                {
                    selectedItem = treeView.SelectedItem;
                    App.LogService.LogDebug("Context menu triggered from TreeView");

                    if (selectedItem is ModInfo selectedMod)
                    {
                        App.LogService.Info($"Deleting mod: {selectedMod.Name}");
                        DeleteModItem(selectedMod);
                    }
                    else if (selectedItem is ModCategory category)
                    {
                        App.LogService.Info($"Deleting category: {category.Name} with {category.Mods.Count} mods");

                        var result = MessageBox.Show(
                            $"Are you sure you want to delete ALL mods in the '{category.Name}' category?\n\nThis action cannot be undone.",
                            "Confirm Category Deletion",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes) return;

                        var modsToDelete = category.Mods.ToList();

                        foreach (var mod in modsToDelete)
                        {
                            DeleteModItem(mod);
                        }
                    }
                }
            }, "Delete mod or category");
        }

        private void DeactivateAllMods_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                // Check which control triggered the context menu
                var menuItem = sender as MenuItem;
                if (menuItem == null) return;

                var contextMenu = menuItem.Parent as ContextMenu;
                if (contextMenu == null) return;

                // For TreeView, deactivate refers to available mods, so we'll handle it differently
                var targetControl = contextMenu.PlacementTarget;
                if (targetControl is TreeView treeView)
                {
                    if (treeView != AppliedModsTreeView)
                    {
                        App.LogService.LogDebug("DeactivateAllMods triggered from AvailableModsTreeView - switching to applied mods tab");
                        // Just switch to applied mods tab if triggered from available mods TreeView
                        ModTabControl.SelectedItem = AppliedModsTab;

                        // Inform user how to deactivate mods
                        if (_appliedMods.Count > 0)
                        {
                            MessageBox.Show(
                                "To deactivate mods, go to the Applied Mods tab and use the checkboxes or right-click menu.",
                                "Deactivate Mods",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        return;
                    }
                }

                // Original functionality for TreeView
                if (_appliedMods.Count == 0)
                {
                    App.LogService.Info("No applied mods to deactivate");
                    MessageBox.Show("There are no applied mods to deactivate.", "No Applied Mods",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    "Are you sure you want to deactivate all mods?",
                    "Confirm Deactivate All Mods",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                App.LogService.Info($"Deactivating all {_appliedMods.Count} mods");
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    foreach (var mod in _appliedMods)
                    {
                        mod.IsActive = false;
                        App.LogService.LogDebug($"Deactivated mod: {mod.Name}");
                    }

                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    // Refresh the TreeView
                    UpdateAppliedModsTreeView();
                    App.LogService.Info("All mods deactivated successfully");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Deactivate all mods");
        }

        private void ActivateAllMods_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                if (_appliedMods.Count == 0)
                {
                    App.LogService.Info("No applied mods to activate");
                    MessageBox.Show("There are no applied mods to activate.", "No Applied Mods",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    "Are you sure you want to activate all mods?",
                    "Confirm Activate All Mods",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                App.LogService.Info($"Activating all {_appliedMods.Count} mods");
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    foreach (var mod in _appliedMods)
                    {
                        mod.IsActive = true;
                        App.LogService.LogDebug($"Activated mod: {mod.Name}");
                    }

                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    // Refresh the TreeView
                    UpdateAppliedModsTreeView();
                    App.LogService.Info("All mods activated successfully");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Activate all mods");
        }


        private void AppliedModsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                DependencyObject originalSource = (DependencyObject)e.OriginalSource;
                while (originalSource != null && !(originalSource is TreeViewItem))
                {
                    originalSource = VisualTreeHelper.GetParent(originalSource);
                }

                if (originalSource is TreeViewItem treeViewItem)
                {
                    if (treeViewItem.DataContext is ModInfo selectedMod)
                    {
                        App.LogService.Info($"Removing mod via double-click: {selectedMod.Name}");

                        selectedMod.IsApplied = false;
                        _appliedMods.Remove(selectedMod);

                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();

                        _configService.MarkModsChanged();
                        SaveAppliedMods();
                    }
                }
            }, "Applied mods tree double-click");
        }

        private List<ModFileConflict> _modConflicts = new List<ModFileConflict>();

        private void DetectModConflicts()
        {
            ErrorHandler.ExecuteSafe(() => {
                using (var perfTracker = new PerformanceTracker("DetectModConflicts"))
                {
                    FlowTracker.StartFlow("DetectConflicts");

                    App.LogService.Info("Detecting mod conflicts");
                    _modConflicts.Clear();

                    var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

                    if (activeMods.Count < 2)
                    {
                        App.LogService.LogDebug("Less than 2 active mods, no conflicts possible");
                        UpdateConflictsListView();
                        FlowTracker.StepFlow("DetectConflicts", "NoConflicts");
                        return;
                    }

                    // Build a map of files to the mods that modify them
                    var fileToModsMap = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
                    int totalFiles = 0;

                    foreach (var mod in activeMods)
                    {
                        App.LogService.LogDebug($"Checking files in mod: {mod.Name}");
                        var modFiles = GetModFiles(mod);
                        totalFiles += modFiles.Count;

                        foreach (var file in modFiles)
                        {
                            if (!fileToModsMap.ContainsKey(file))
                            {
                                fileToModsMap[file] = new List<ModInfo>();
                            }
                            fileToModsMap[file].Add(mod);
                        }
                    }

                    // Find conflicts (files modified by multiple mods)
                    foreach (var entry in fileToModsMap)
                    {
                        if (entry.Value.Count > 1)
                        {
                            App.LogService.LogDebug($"Found conflict for file: {entry.Key} between {entry.Value.Count} mods");
                            _modConflicts.Add(new ModFileConflict
                            {
                                FilePath = entry.Key,
                                ConflictingMods = entry.Value.ToList()
                            });
                        }
                    }

                    UpdateConflictsListView();
                    HighlightWinningMods();

                    App.LogService.Info($"Conflict detection complete. Found {_modConflicts.Count} conflicts across {totalFiles} total files");
                    App.LogService.LogDebug($"Total active mods analyzed: {activeMods.Count}");

                    FlowTracker.StepFlow("DetectConflicts", "Complete");
                    FlowTracker.EndFlow("DetectConflicts");
                }
            }, "Detect mod conflicts");
        }

        private void UpdateConflictsListView()
        {
            ErrorHandler.ExecuteSafe(() => {
                var conflictItems = new List<ConflictItem>();

                foreach (var conflict in _modConflicts)
                {
                    foreach (var mod in conflict.ConflictingMods)
                    {
                        conflictItems.Add(new ConflictItem
                        {
                            Mod = mod,
                            FilePath = conflict.FilePath
                        });
                    }
                }

                conflictItems = conflictItems.OrderBy(c => c.FilePath).ThenBy(c => c.ModName).ToList();

                ConflictsListView.ItemsSource = conflictItems;
                App.LogService.LogDebug($"Updated conflicts list view with {conflictItems.Count} items");

                if (conflictItems.Count > 0)
                {
                    var parent = VisualTreeHelper.GetParent(ConflictsListView) as Grid;
                    if (parent != null)
                    {
                        var noConflictsMessage = parent.Children.OfType<TextBlock>()
                            .FirstOrDefault(tb => tb.Name == "NoConflictsMessage");

                        if (noConflictsMessage != null)
                        {
                            parent.Children.Remove(noConflictsMessage);
                        }
                    }

                    ConflictsListView.Visibility = Visibility.Visible;
                }
                else
                {
                    var parent = VisualTreeHelper.GetParent(ConflictsListView) as Grid;
                    bool messageExists = false;

                    if (parent != null)
                    {
                        messageExists = parent.Children.OfType<TextBlock>()
                            .Any(tb => tb.Name == "NoConflictsMessage");
                    }

                    if (messageExists)
                    {
                        ConflictsListView.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ConflictsListView.Visibility = Visibility.Visible;
                    }
                }

                UpdateConflictTabBadge(conflictItems.Count);
            }, "Update conflicts list view");
        }

        private List<string> GetModFiles(ModInfo mod)
        {
            return ErrorHandler.ExecuteSafe(() => {
                var files = new List<string>();

                try
                {
                    App.LogService.LogDebug($"Getting files for mod: {mod.Name}");

                    if (mod.IsFromArchive)
                    {
                        files = GetFilesFromArchive(mod.ArchiveSource, mod.ArchiveRootPath);
                        App.LogService.LogDebug($"Found {files.Count} files in archive mod");
                    }
                    else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                    {
                        files = GetFilesFromFolder(mod.ModFilesPath);
                        App.LogService.LogDebug($"Found {files.Count} files in folder mod");
                    }
                    else
                    {
                        App.LogService.Warning($"Mod has no valid files path: {mod.Name}");
                    }
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Error getting files for mod {mod.Name}: {ex.Message}");
                }

                return files;
            }, $"Get files for mod: {mod.Name}", false);
        }

        private List<string> GetFilesFromArchive(string archivePath, string rootPath)
        {
            return ErrorHandler.ExecuteSafe(() => {
            var files = new List<string>();

            if (!File.Exists(archivePath))
            {
                App.LogService.Warning($"Archive file not found: {archivePath}");
                return files;
            }

            try
            {
                App.LogService.LogDebug($"Reading files from archive: {archivePath}");

                using (var archive = SharpCompress.Archives.ArchiveFactory.Open(archivePath))
                {
                    string modPath = rootPath;
                    if (!string.IsNullOrEmpty(modPath))
                    {
                        modPath = Path.Combine(modPath, "Mod");
                        modPath = modPath.Replace('\\', '/');
                    }
                    else
                    {
                        modPath = "Mod";
                    }

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue;

                        string entryKey = entry.Key.Replace('\\', '/');
                        if (entryKey.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            string relativePath = entryKey.Substring(modPath.Length + 1);
                            files.Add(relativePath);
                        }
                    }

                    App.LogService.LogDebug($"Found {files.Count} files in archive");
                }
            }
                catch (Exception ex)
                {
                    LogCategorizedError($"Error reading archive {archivePath}", ex, ErrorCategory.FileSystem);
                }

                return files;
            }, $"Get files from archive: {Path.GetFileName(archivePath)}", false);
        }

        private List<string> GetFilesFromFolder(string folderPath)
        {
            return ErrorHandler.ExecuteSafe(() => {
                var files = new List<string>();

                if (!Directory.Exists(folderPath))
                {
                    App.LogService.Warning($"Mod folder not found: {folderPath}");
                    return files;
                }

                try
                {
                    App.LogService.LogDebug($"Reading files from folder: {folderPath}");

                    string baseDir = folderPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    int baseDirLength = baseDir.Length;

                    foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = file.Substring(baseDirLength).Replace('\\', '/');
                        files.Add(relativePath);
                    }

                    App.LogService.LogDebug($"Found {files.Count} files in folder");
                }
                catch (Exception ex)
                {
                    LogCategorizedError($"Error reading folder {folderPath}", ex, ErrorCategory.FileSystem);
                }

                return files;
            }, $"Get files from folder: {Path.GetFileName(folderPath)}", false);
        }

        private void UpdateConflictTabBadge(int conflictCount)
        {
            ErrorHandler.ExecuteSafe(() => {
                var conflictHeader = ConflictsTab.Header as TextBlock;
                if (conflictHeader != null)
                {
                    if (conflictCount > 0)
                    {
                        conflictHeader.Text = $"Conflicts ({conflictCount})";
                        conflictHeader.FontWeight = FontWeights.DemiBold;
                    }
                    else
                    {
                        conflictHeader.Text = "Conflicts";
                        conflictHeader.FontWeight = FontWeights.DemiBold;
                    }

                    if (conflictHeader.Style == null)
                    {
                        conflictHeader.Style = FindResource("TabHeaderTextStyle") as Style;
                    }

                    conflictHeader.ClearValue(TextBlock.ForegroundProperty);
                }
            }, "Update conflict tab badge");
        }

        private void HighlightWinningMods()
        {
            ErrorHandler.ExecuteSafe(() => {
                if (ConflictsListView.ItemsSource == null) return;

                var conflictItems = ConflictsListView.ItemsSource as List<ConflictItem>;
                if (conflictItems == null || !conflictItems.Any()) return;

                App.LogService.LogDebug("Highlighting winning mods in conflicts view");

                var conflictsByFile = conflictItems.GroupBy(c => c.FilePath).ToList();

                foreach (var fileGroup in conflictsByFile)
                {
                    // The winning mod is the one loaded last (highest in the applied mods list)
                    var winningMod = fileGroup
                        .Select(c => c.Mod)
                        .OrderByDescending(m => _appliedMods.IndexOf(m))
                        .FirstOrDefault();

                    if (winningMod != null)
                    {
                        App.LogService.LogDebug($"Winning mod for file {fileGroup.Key}: {winningMod.Name}");

                        foreach (var item in conflictItems.Where(c => c.FilePath == fileGroup.Key))
                        {
                            item.IsWinningMod = item.Mod == winningMod;
                        }
                    }
                }

                ConflictsListView.Items.Refresh();
            }, "Highlight winning mods");
        }

        private void AddConflictsContextMenu()
        {
            ErrorHandler.ExecuteSafe(() => {
                var contextMenu = new ContextMenu();

                var jumpToModMenuItem = new MenuItem
                {
                    Header = "Go to this mod in Applied Mods",
                    Style = (Style)FindResource("ModContextMenuItemStyle")
                };
                jumpToModMenuItem.Click += JumpToModMenuItem_Click;

                var highlightMenuItem = new MenuItem
                {
                    Header = "Highlight conflicting files",
                    Style = (Style)FindResource("ModContextMenuItemStyle")
                };
                highlightMenuItem.Click += HighlightConflictingFiles_Click;

                contextMenu.Items.Add(jumpToModMenuItem);
                contextMenu.Items.Add(highlightMenuItem);

                ConflictsListView.ContextMenu = contextMenu;

                App.LogService.LogDebug("Added context menu to conflicts list view");
            }, "Add conflicts context menu");
        }

        private void JumpToModMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
                if (selectedItem == null || selectedItem.Mod == null) return;

                App.LogService.LogDebug($"Jumping to mod in applied mods tab: {selectedItem.Mod.Name}");

                ModTabControl.SelectedItem = AppliedModsTab;

                // Find and select the mod in the TreeView instead of ListView
                SelectModInTreeView(selectedItem.Mod);
            }, "Jump to mod from conflicts");
        }

        private void HighlightConflictingFiles_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
                if (selectedItem == null) return;

                string filePath = selectedItem.FilePath;
                App.LogService.LogDebug($"Highlighting all conflicts for file: {filePath}");

                foreach (var item in ConflictsListView.Items)
                {
                    if (item is ConflictItem conflictItem && conflictItem.FilePath == filePath)
                    {
                        ConflictsListView.SelectedItems.Add(item);
                    }
                }
            }, "Highlight conflicting files");
        }

        private void InitializeConflictSystem()
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug("Initializing conflict detection system");

                AddConflictsContextMenu();
                ConflictsListView.MouseDoubleClick += (s, e) =>
                {
                    var originalSource = e.OriginalSource as DependencyObject;
                    while (originalSource != null && !(originalSource is ListViewItem))
                    {
                        originalSource = VisualTreeHelper.GetParent(originalSource);
                    }

                    if (originalSource is ListViewItem)
                    {
                        JumpToModMenuItem_Click(s, e);
                    }
                };
            }, "Initialize conflict system");
        }

        private void DeleteModItem(ModInfo mod)
        {
            ErrorHandler.ExecuteSafe(() => {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{mod.Name}' from your system?\n\nThis action cannot be undone.",
                    "Confirm Mod Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                FlowTracker.StartFlow("DeleteMod");
                App.LogService.Info($"Deleting mod: {mod.Name}");

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    if (mod.IsApplied)
                    {
                        FlowTracker.StepFlow("DeleteMod", "RemoveFromApplied");
                        App.LogService.LogDebug($"Removing mod from applied list first");

                        mod.IsApplied = false;
                        _appliedMods.Remove(mod);

                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        UpdateAppliedModsTreeView();
                    }

                    FlowTracker.StepFlow("DeleteMod", "RemoveFromList");
                    _availableModsFlat.Remove(mod);

                    FlowTracker.StepFlow("DeleteMod", "DeleteFiles");
                    if (mod.IsFromArchive)
                    {
                        if (!string.IsNullOrEmpty(mod.ArchiveSource) && File.Exists(mod.ArchiveSource))
                        {
                            App.LogService.LogDebug($"Deleting archive file: {mod.ArchiveSource}");
                            File.Delete(mod.ArchiveSource);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(mod.ModFolderPath) && Directory.Exists(mod.ModFolderPath))
                        {
                            App.LogService.LogDebug($"Deleting mod folder: {mod.ModFolderPath}");
                            Directory.Delete(mod.ModFolderPath, true);
                        }
                    }

                    FlowTracker.StepFlow("DeleteMod", "UpdateUI");
                    _availableModsCategories = _availableModsFlat.GroupByCategory();
                    AvailableModsTreeView.ItemsSource = _availableModsCategories;

                    _configService.MarkModsChanged();
                    SaveAppliedMods();

                    App.LogService.Info($"Successfully deleted mod: {mod.Name}");
                    FlowTracker.StepFlow("DeleteMod", "Complete");
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("DeleteMod", "Error");
                    LogCategorizedError($"Error deleting mod '{mod.Name}'", ex, ErrorCategory.FileSystem);
                    MessageBox.Show($"Error deleting mod '{mod.Name}': {ex.Message}", "Delete Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("DeleteMod");
                }
            }, "Delete mod item");
        }

        private void InitializeProfiles()
        {
            ErrorHandler.ExecuteSafe(() => {
                FlowTracker.StartFlow("InitializeProfiles");

                try
                {
                    App.LogService.Info("Initializing profiles");

                    _profiles.Clear();

                    if (_currentGame != null)
                    {
                        string normalizedGameId = GetNormalizedCurrentGameId();
                        App.LogService.LogDebug($"Getting profiles for normalized game ID: {normalizedGameId}");

                        var gameProfiles = _profileService.GetProfilesForGame(normalizedGameId);

                        if (gameProfiles != null && gameProfiles.Count > 0)
                        {
                            foreach (var profile in gameProfiles)
                            {
                                if (profile != null)
                                {
                                    _profiles.Add(profile);
                                }
                            }
                            App.LogService.LogDebug($"Loaded {_profiles.Count} profiles for game {_currentGame.Name}");
                        }
                        else
                        {
                            App.LogService.LogDebug("No profiles found, creating default profile");
                            var defaultProfile = new ModProfile();
                            _profiles.Add(defaultProfile);

                            _profileService.ImportProfileDirectAsync(normalizedGameId, defaultProfile).ConfigureAwait(false);
                        }

                        FlowTracker.StepFlow("InitializeProfiles", "GetActiveProfile");
                        _activeProfile = _profileService.GetFullyLoadedActiveProfile(normalizedGameId);

                        App.LogService.LogDebug($"Got active profile: {_activeProfile.Name} (ID: {_activeProfile.Id})");
                        App.LogService.LogDebug($"Active profile has {_activeProfile.AppliedMods?.Count ?? 0} mods");
                    }
                    else
                    {
                        App.LogService.LogDebug("No game selected, using default profile");
                        _profiles.Add(new ModProfile { Name = "Default Profile" });
                        _activeProfile = _profiles[0];
                    }

                    Dispatcher.Invoke(() => {
                        UpdateProfileComboBox();
                    });

                    App.LogService.Info("Profile initialization complete");
                    FlowTracker.StepFlow("InitializeProfiles", "Complete");
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("InitializeProfiles", "Error");
                    LogCategorizedError("Error initializing profiles", ex, ErrorCategory.ProfileManagement);

                    try
                    {
                        App.LogService.LogDebug("Creating fallback profile after error");
                        _profiles.Clear();
                        _profiles.Add(new ModProfile { Name = "Default Profile" });
                        _activeProfile = _profiles[0];

                        Dispatcher.Invoke(() => {
                            UpdateProfileComboBox();
                        });
                    }
                    catch (Exception innerEx)
                    {
                        App.LogService.Error("Failed to create fallback profile: " + innerEx.Message);
                    }
                }
                finally
                {
                    FlowTracker.EndFlow("InitializeProfiles");
                }
            }, "Initialize profiles");
        }

        private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("ProfileChange");

                App.LogService.Info("Profile selection changed");

                if (_currentGame == null || _profiles.Count == 0 || ProfileComboBox.SelectedIndex < 0)
                {
                    App.LogService.LogDebug("Profile change skipped - no game, no profiles, or no selection");
                    FlowTracker.EndFlow("ProfileChange");
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < _profiles.Count)
                {
                    ModProfile selectedProfile = _profiles[selectedIndex];

                    if (_activeProfile != null && selectedProfile.Id == _activeProfile.Id)
                    {
                        App.LogService.LogDebug($"Already on profile: {selectedProfile.Name}, no change needed");
                        FlowTracker.EndFlow("ProfileChange");
                        return;
                    }

                    App.LogService.Info($"Switching from '{_activeProfile?.Name}' to '{selectedProfile.Name}'");

                    try
                    {
                        FlowTracker.StepFlow("ProfileChange", "PrepareSwitching");
                        Mouse.OverrideCursor = Cursors.Wait;

                        if (_activeProfile != null)
                        {
                            App.LogService.LogDebug($"Saving current profile '{_activeProfile.Name}' before switching");
                            await SaveCurrentModsToActiveProfile();
                            App.LogService.LogDebug("Save completed");
                        }

                        App.LogService.LogDebug($"Setting new active profile: {selectedProfile.Name}");

                        _activeProfile = selectedProfile;

                        string normalizedGameId = GetNormalizedCurrentGameId();

                        using (var perfTracker = new PerformanceTracker("SetActiveProfile"))
                        {
                            await _profileService.SetActiveProfileAsync(normalizedGameId, _activeProfile.Id);
                        }

                        FlowTracker.StepFlow("ProfileChange", "LoadModsFromProfile");
                        LoadAppliedModsFromProfile(_activeProfile);

                        App.LogService.Info("Profile switch complete");
                        FlowTracker.StepFlow("ProfileChange", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("ProfileChange", "Error");
                        LogCategorizedError("Error during profile switch", ex, ErrorCategory.ProfileManagement);
                        MessageBox.Show($"Error switching profiles: {ex.Message}", "Profile Switch Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                        FlowTracker.EndFlow("ProfileChange");
                    }
                }
            }, "Profile selection changed");
        }

        private string ShowInputDialog(string title, string message, string defaultValue = "")
        {
            return ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug($"Showing input dialog: {title}");

                Window dialog = new Window
                {
                    Title = title,
                    Width = 350,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = this,
                    Background = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"]
                };

                StackPanel panel = new StackPanel { Margin = new Thickness(10) };

                panel.Children.Add(new TextBlock
                {
                    Text = message,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"]
                });

                TextBox textBox = new TextBox
                {
                    Text = defaultValue,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(5),
                    Background = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"],
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"],
                    BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51))
                };
                panel.Children.Add(textBox);

                StackPanel buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var buttonHeight = 32;
                var buttonPadding = new Thickness(8, 4, 8, 4);

                Button cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 80,
                    Height = buttonHeight,
                    Padding = buttonPadding,
                    Margin = new Thickness(0, 0, 10, 0),
                    Style = (Style)Application.Current.Resources["ActionButton"],
                    IsCancel = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                Button okButton = new Button
                {
                    Content = "OK",
                    Width = 80,
                    Height = buttonHeight,
                    Padding = buttonPadding,
                    Style = (Style)Application.Current.Resources["PrimaryButton"],
                    Foreground = Brushes.Black,
                    IsDefault = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                okButton.Click += (s, e) => { dialog.DialogResult = true; };

                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(okButton);
                panel.Children.Add(buttonPanel);

                dialog.Content = panel;
                dialog.Loaded += (s, e) =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                };

                bool? result = dialog.ShowDialog();
                string userInput = result == true ? textBox.Text : null;

                App.LogService.LogDebug($"Input dialog result: {(result == true ? "OK" : "Cancel")}");

                return userInput;
            }, "Show input dialog");
        }

        private void InitializeUi()
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug("Initializing profile UI components");

                ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
                if (NewProfileButton != null)
                {
                    NewProfileButton.Click += NewProfileButton_Click;
                    App.LogService.LogDebug("New Profile button wired up");
                }

                if (DeleteProfileButton != null)
                {
                    DeleteProfileButton.Click += DeleteProfileButton_Click;
                    App.LogService.LogDebug("Delete Profile button wired up");
                }

                if (ExportProfileButton != null)
                {
                    ExportProfileButton.Click += ExportProfileButton_Click;
                    App.LogService.LogDebug("Export Profile button wired up");
                }

                if (ImportProfileButton != null)
                {
                    ImportProfileButton.Click += ImportProfileButton_Click;
                    App.LogService.LogDebug("Import Profile button wired up");
                }

                App.LogService.LogDebug("Profile UI initialization complete");
            }, "Initialize UI");
        }

        private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("CreateNewProfile");

                App.LogService.Info("New Profile button clicked");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot create profile - no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("CreateNewProfile", "NoGame");
                    return;
                }

                string profileName = ShowInputDialog("New Profile", "Enter profile name:", "New Profile");

                if (string.IsNullOrEmpty(profileName))
                {
                    App.LogService.LogDebug("User cancelled or entered empty name");
                    FlowTracker.StepFlow("CreateNewProfile", "Cancelled");
                    return;
                }

                App.LogService.Info($"Creating new profile: {profileName}");
                FlowTracker.StepFlow("CreateNewProfile", "Creating");

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    // Save existing profile before creating new one
                    if (_activeProfile != null)
                    {
                        App.LogService.LogDebug($"Saving current active profile '{_activeProfile?.Name}' before creating new one");
                        await SaveCurrentModsToActiveProfile();
                    }

                    string normalizedGameId = GetNormalizedCurrentGameId();
                    App.LogService.LogDebug($"Using normalized game ID: {normalizedGameId}");

                    using (var perfTracker = new PerformanceTracker("CreateProfile"))
                    {
                        var newProfile = await _profileService.CreateProfileAsync(normalizedGameId, profileName);

                        if (newProfile != null)
                        {
                            App.LogService.LogDebug($"New profile created: {newProfile.Name} with ID {newProfile.Id}");

                            await _profileService.SaveProfilesAsync();

                            string oldActiveProfileId = _activeProfile?.Id;

                            _activeProfile = newProfile;
                            await _profileService.SetActiveProfileAsync(normalizedGameId, _activeProfile.Id);

                            FlowTracker.StepFlow("CreateNewProfile", "UpdateUI");
                            InitializeProfiles();

                            int newProfileIndex = _profiles.FindIndex(p => p.Id == newProfile.Id);

                            ProfileComboBox.SelectionChanged -= ProfileComboBox_SelectionChanged;

                            if (newProfileIndex >= 0 && newProfileIndex < ProfileComboBox.Items.Count)
                            {
                                ProfileComboBox.SelectedIndex = newProfileIndex;
                            }

                            ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

                            _appliedMods.Clear();
                            UpdateAppliedModsTreeView();

                            MessageBox.Show($"Profile '{newProfile.Name}' created successfully.", "Profile Created",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                            App.LogService.Info($"Profile '{newProfile.Name}' created successfully");
                            FlowTracker.StepFlow("CreateNewProfile", "Complete");
                        }
                        else
                        {
                            App.LogService.Error("Failed to create profile");
                            MessageBox.Show("Failed to create the profile. Please try again.", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            FlowTracker.StepFlow("CreateNewProfile", "Failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("CreateNewProfile", "Error");
                    LogCategorizedError("Error creating profile", ex, ErrorCategory.ProfileManagement);
                    MessageBox.Show($"Error creating profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("CreateNewProfile");
                }
            }, "Create new profile");
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("DeleteProfile");

                App.LogService.Info("Delete Profile button clicked");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot delete profile - no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("DeleteProfile", "NoGame");
                    return;
                }

                if (_profiles.Count <= 1)
                {
                    App.LogService.Warning("Cannot delete the only profile");
                    MessageBox.Show("Cannot delete the only profile. At least one profile must exist.",
                        "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                    FlowTracker.StepFlow("DeleteProfile", "OnlyProfile");
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= _profiles.Count)
                {
                    App.LogService.Warning("No profile selected for deletion");
                    FlowTracker.StepFlow("DeleteProfile", "NoSelection");
                    return;
                }

                ModProfile selectedProfile = _profiles[selectedIndex];
                App.LogService.LogDebug($"Selected profile for deletion: {selectedProfile.Name} (ID: {selectedProfile.Id})");

                var result = MessageBox.Show($"Are you sure you want to delete the profile '{selectedProfile.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    App.LogService.LogDebug("User cancelled deletion");
                    FlowTracker.StepFlow("DeleteProfile", "Cancelled");
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    FlowTracker.StepFlow("DeleteProfile", "Deleting");
                    App.LogService.Info($"Deleting profile {selectedProfile.Name} with ID {selectedProfile.Id}");

                    App.LogService.LogDebug($"Current profiles in memory:");
                    foreach (var profile in _profiles)
                    {
                        App.LogService.LogDebug($"  - {profile.Name} (ID: {profile.Id})");
                    }

                    using (var perfTracker = new PerformanceTracker("DeleteProfile"))
                    {
                        bool deleted = await _profileService.DeleteProfileAsync(_currentGame.Id, selectedProfile.Id);
                        App.LogService.LogDebug($"DeleteProfileAsync returned: {deleted}");

                        if (deleted)
                        {
                            FlowTracker.StepFlow("DeleteProfile", "Success");
                            App.LogService.Info($"Profile deleted: {selectedProfile.Name}");

                            await _profileService.SaveProfilesAsync();
                            App.LogService.LogDebug("Profiles saved after deletion");

                            InitializeProfiles();

                            if (_activeProfile != null)
                            {
                                LoadAppliedModsFromProfile(_activeProfile);
                            }

                            MessageBox.Show($"Profile '{selectedProfile.Name}' deleted successfully.", "Profile Deleted",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                            FlowTracker.StepFlow("DeleteProfile", "Complete");
                        }
                        else
                        {
                            FlowTracker.StepFlow("DeleteProfile", "Failed");
                            App.LogService.Error("Failed to delete profile - service returned false");
                            MessageBox.Show("Failed to delete the profile. Please try again.", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    FlowTracker.StepFlow("DeleteProfile", "Error");
                    LogCategorizedError("Error deleting profile", ex, ErrorCategory.ProfileManagement);
                    MessageBox.Show($"Error deleting profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("DeleteProfile");
                }
            }, "Delete profile");
        }

        private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("ExportProfile");

                App.LogService.Info("Export Profile button clicked");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot export profile - no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("ExportProfile", "NoGame");
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= _profiles.Count)
                {
                    App.LogService.Warning("No profile selected for export");
                    MessageBox.Show("Please select a profile to export.", "No Profile Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("ExportProfile", "NoSelection");
                    return;
                }

                ModProfile selectedProfile = _profiles[selectedIndex];
                App.LogService.LogDebug($"Selected profile for export: {selectedProfile.Name} (ID: {selectedProfile.Id})");

                try
                {
                    FlowTracker.StepFlow("ExportProfile", "SaveCurrentState");
                    // Save current state before export
                    var currentMods = _appliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.IsFromArchive ? null : PathUtility.ToRelativePath(m.ModFolderPath),
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.IsFromArchive ? PathUtility.ToRelativePath(m.ArchiveSource) : null,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList();

                    await _profileService.UpdateActiveProfileModsAsync(_currentGame.Id, currentMods);
                    App.LogService.LogDebug("Saved current mods to profile before export");
                }
                catch (Exception ex)
                {
                    App.LogService.Warning($"Error saving current profile before export: {ex.Message}");
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Profile",
                    Filter = "JSON Files (*.json)|*.json",
                    DefaultExt = ".json",
                    FileName = $"{_currentGame.Name}_{selectedProfile.Name}_{DateTime.Now:yyyyMMdd}.json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                FlowTracker.StepFlow("ExportProfile", "ShowFileDialog");
                if (saveFileDialog.ShowDialog() == true)
                {
                    App.LogService.LogDebug($"Selected export file: {saveFileDialog.FileName}");

                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        FlowTracker.StepFlow("ExportProfile", "ExportingData");
                        var exportProfile = CreateExportCopy(selectedProfile);

                        App.LogService.LogDebug($"Exporting profile {selectedProfile.Name} to {saveFileDialog.FileName}");

                        string json = System.Text.Json.JsonSerializer.Serialize(exportProfile, new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                        File.WriteAllText(saveFileDialog.FileName, json);
                        App.LogService.LogDebug($"Profile JSON written to file successfully");

                        Mouse.OverrideCursor = null;

                        App.LogService.Info($"Profile exported to: {saveFileDialog.FileName}");
                        MessageBox.Show($"Profile '{selectedProfile.Name}' exported successfully.", "Profile Exported",
                            MessageBoxButton.OK, MessageBoxImage.Information);

                        FlowTracker.StepFlow("ExportProfile", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("ExportProfile", "Error");
                        Mouse.OverrideCursor = null;

                        LogCategorizedError("Error exporting profile", ex, ErrorCategory.FileSystem);
                        MessageBox.Show($"Error exporting profile: {ex.Message}", "Export Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    App.LogService.LogDebug("Export cancelled by user");
                    FlowTracker.StepFlow("ExportProfile", "Cancelled");
                }

                FlowTracker.EndFlow("ExportProfile");
            }, "Export profile");
        }

        private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("ImportProfile");

                App.LogService.Info("Import Profile button clicked");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot import profile - no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("ImportProfile", "NoGame");
                    return;
                }

                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Profile",
                    Filter = "JSON Files (*.json)|*.json",
                    DefaultExt = ".json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                FlowTracker.StepFlow("ImportProfile", "ShowFileDialog");
                if (openFileDialog.ShowDialog() == true)
                {
                    App.LogService.LogDebug($"Selected import file: {openFileDialog.FileName}");

                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        FlowTracker.StepFlow("ImportProfile", "ReadingFile");
                        App.LogService.LogDebug($"Reading file content from {openFileDialog.FileName}");

                        string json = File.ReadAllText(openFileDialog.FileName);

                        var importedProfile = System.Text.Json.JsonSerializer.Deserialize<ModProfile>(json);

                        if (importedProfile == null)
                        {
                            throw new Exception("Failed to deserialize profile data");
                        }

                        FlowTracker.StepFlow("ImportProfile", "ProcessingProfile");
                        string oldId = importedProfile.Id;
                        importedProfile.Id = Guid.NewGuid().ToString();
                        importedProfile.LastModified = DateTime.Now;

                        App.LogService.LogDebug($"Imported profile: {importedProfile.Name} with {importedProfile.AppliedMods?.Count ?? 0} mods");
                        App.LogService.LogDebug($"Changed ID from {oldId} to {importedProfile.Id}");

                        // Ensure unique name
                        string baseName = importedProfile.Name;
                        int counter = 1;

                        while (_profiles.Any(p => p.Name == importedProfile.Name))
                        {
                            importedProfile.Name = $"{baseName} (Imported {counter++})";
                        }

                        if (importedProfile.AppliedMods == null)
                        {
                            importedProfile.AppliedMods = new List<AppliedModSetting>();
                        }

                        FlowTracker.StepFlow("ImportProfile", "SaveProfile");
                        using (var perfTracker = new PerformanceTracker("ImportProfile"))
                        {
                            var imported = await _profileService.ImportProfileDirectAsync(_currentGame.Id, importedProfile);

                            if (imported == null)
                            {
                                throw new Exception("Failed to import profile to service");
                            }

                            await _profileService.SetActiveProfileAsync(_currentGame.Id, imported.Id);

                            App.LogService.LogDebug($"Added and set as active profile: {imported.Name}");

                            await _profileService.SaveProfilesAsync();
                            App.LogService.LogDebug("Profiles saved to storage after import");

                            InitializeProfiles();
                            LoadProfilesIntoDropdown();

                            _activeProfile = imported;
                            LoadAppliedModsFromProfile(_activeProfile);
                        }

                        Mouse.OverrideCursor = null;

                        App.LogService.Info($"Profile imported successfully");
                        MessageBox.Show($"Profile '{importedProfile.Name}' imported successfully.", "Profile Imported",
                            MessageBoxButton.OK, MessageBoxImage.Information);

                        FlowTracker.StepFlow("ImportProfile", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("ImportProfile", "Error");
                        Mouse.OverrideCursor = null;

                        LogCategorizedError("Error importing profile", ex, ErrorCategory.FileSystem);
                        MessageBox.Show($"Error importing profile: {ex.Message}", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    App.LogService.LogDebug("Import cancelled by user");
                    FlowTracker.StepFlow("ImportProfile", "Cancelled");
                }

                FlowTracker.EndFlow("ImportProfile");
            }, "Import profile");
        }

        private void LoadAppliedModsFromProfile(ModProfile profile)
        {
            ErrorHandler.ExecuteSafe(() => {
                using (var perfTracker = new PerformanceTracker("LoadAppliedModsFromProfile"))
                {
                    FlowTracker.StartFlow("LoadProfileMods");

                    try
                    {
                        App.LogService.Info($"Loading mods from profile: {profile.Name} (ID: {profile.Id})");

                        _appliedMods.Clear();

                        if (profile == null)
                        {
                            App.LogService.Warning("Profile is null");
                            AppliedModsTreeView.Visibility = Visibility.Collapsed;
                            FlowTracker.StepFlow("LoadProfileMods", "NullProfile");
                            return;
                        }

                        if (profile.AppliedMods == null)
                        {
                            profile.AppliedMods = new List<AppliedModSetting>();
                            App.LogService.LogDebug("Profile had null AppliedMods list, initialized as empty");
                        }

                        if (profile.AppliedMods.Count == 0)
                        {
                            App.LogService.LogDebug("No mods in profile");
                            AppliedModsTreeView.Visibility = Visibility.Collapsed;
                            FlowTracker.StepFlow("LoadProfileMods", "EmptyProfile");
                            return;
                        }

                        App.LogService.LogDebug($"Profile contains {profile.AppliedMods.Count} mods");

                        // Log detailed mod information at trace level
                        foreach (var mod in profile.AppliedMods)
                        {
                            App.LogService.Trace($"  Profile mod: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}, Active: {mod.IsActive}");
                        }

                        FlowTracker.StepFlow("LoadProfileMods", "BuildModIndex");
                        Dictionary<string, ModInfo> availableMods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
                        foreach (var mod in _availableModsFlat)
                        {
                            if (mod.IsFromArchive && !string.IsNullOrEmpty(mod.ArchiveSource))
                            {
                                string key = PathUtility.ToAbsolutePath(mod.ArchiveSource);
                                if (!availableMods.ContainsKey(key))
                                {
                                    availableMods[key] = mod;
                                }
                            }

                            if (!string.IsNullOrEmpty(mod.ModFolderPath))
                            {
                                string key = PathUtility.ToAbsolutePath(mod.ModFolderPath);
                                if (!availableMods.ContainsKey(key))
                                {
                                    availableMods[key] = mod;
                                }
                            }
                        }

                        int loadedModCount = 0;
                        int failedModCount = 0;

                        FlowTracker.StepFlow("LoadProfileMods", "ProcessMods");

                        foreach (var setting in profile.AppliedMods)
                        {
                            try
                            {
                                if (setting == null)
                                {
                                    App.LogService.LogDebug("Skipping null mod setting");
                                    continue;
                                }

                                string searchPath = setting.IsFromArchive
                                    ? PathUtility.ToAbsolutePath(setting.ArchiveSource)
                                    : PathUtility.ToAbsolutePath(setting.ModFolderPath);

                                if (string.IsNullOrEmpty(searchPath))
                                {
                                    App.LogService.LogDebug("Skipping mod with empty path");
                                    continue;
                                }

                                App.LogService.Trace($"Looking for mod: {searchPath}");

                                if (availableMods.TryGetValue(searchPath, out var mod))
                                {
                                    mod.IsApplied = true;
                                    mod.IsActive = setting.IsActive;
                                    _appliedMods.Add(mod);
                                    loadedModCount++;
                                    App.LogService.Trace($"Added mod to applied list from index: {mod.Name}");
                                    continue;
                                }

                                if (setting.IsFromArchive && !string.IsNullOrEmpty(setting.ArchiveSource))
                                {
                                    App.LogService.LogDebug($"Mod not found in available mods, trying to load archive directly");

                                    try
                                    {
                                        string absoluteArchivePath = PathUtility.ToAbsolutePath(setting.ArchiveSource);
                                        App.LogService.LogDebug($"Absolute archive path: {absoluteArchivePath}");

                                        if (File.Exists(absoluteArchivePath))
                                        {
                                            var archiveTask = _modDetectionService.LoadModFromArchivePathAsync(
                                                absoluteArchivePath, _currentGame.Name, setting.ArchiveRootPath);

                                            if (archiveTask.Wait(3000))
                                            {
                                                var archiveMod = archiveTask.Result;
                                                if (archiveMod != null)
                                                {
                                                    archiveMod.IsApplied = true;
                                                    archiveMod.IsActive = setting.IsActive;
                                                    _appliedMods.Add(archiveMod);
                                                    loadedModCount++;
                                                    App.LogService.LogDebug($"Added archive mod: {archiveMod.Name}");
                                                }
                                                else
                                                {
                                                    App.LogService.Warning("Archive mod loading returned null");
                                                    failedModCount++;
                                                }
                                            }
                                            else
                                            {
                                                App.LogService.Warning("Archive loading timed out");
                                                failedModCount++;
                                            }
                                        }
                                        else
                                        {
                                            App.LogService.Warning($"Archive file not found: {absoluteArchivePath}");
                                            failedModCount++;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        App.LogService.Error($"Error loading archive mod: {ex.Message}");
                                        failedModCount++;
                                    }
                                }
                                else if (!string.IsNullOrEmpty(setting.ModFolderPath))
                                {
                                    App.LogService.LogDebug($"Mod not found in available mods, trying to load from folder directly");

                                    try
                                    {
                                        string absoluteFolderPath = PathUtility.ToAbsolutePath(setting.ModFolderPath);
                                        App.LogService.LogDebug($"Absolute folder path: {absoluteFolderPath}");

                                        if (Directory.Exists(absoluteFolderPath))
                                        {
                                            var folderMod = _modDetectionService.LoadModFromFolderPath(absoluteFolderPath, _currentGame.Name);
                                            if (folderMod != null)
                                            {
                                                folderMod.IsApplied = true;
                                                folderMod.IsActive = setting.IsActive;
                                                _appliedMods.Add(folderMod);
                                                loadedModCount++;
                                                App.LogService.LogDebug($"Added folder mod: {folderMod.Name}");
                                            }
                                            else
                                            {
                                                App.LogService.Warning("Folder mod loading returned null");
                                                failedModCount++;
                                            }
                                        }
                                        else
                                        {
                                            App.LogService.Warning($"Mod folder not found: {absoluteFolderPath}");
                                            failedModCount++;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        App.LogService.Error($"Error loading folder mod: {ex.Message}");
                                        failedModCount++;
                                    }
                                }
                                else
                                {
                                    App.LogService.Warning("Could not find or load mod - missing path information");
                                    failedModCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogService.Error($"Error adding mod from profile: {ex.Message}");
                                failedModCount++;
                            }
                        }

                        FlowTracker.StepFlow("LoadProfileMods", "UpdateUI");

                        // Update the TreeView with the loaded mods
                        UpdateAppliedModsTreeView();

                        UpdateModPriorities();
                        UpdateModPriorityDisplays();
                        DetectModConflicts();

                        if (failedModCount > 0)
                        {
                            App.LogService.Warning($"Failed to load {failedModCount} mods from profile");
                        }

                        App.LogService.Info($"Loaded {loadedModCount} mods from profile '{profile.Name}'");
                        FlowTracker.StepFlow("LoadProfileMods", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("LoadProfileMods", "Error");
                        LogCategorizedError("Error loading mods from profile", ex, ErrorCategory.ProfileManagement);
                    }
                    finally
                    {
                        FlowTracker.EndFlow("LoadProfileMods");
                    }
                }
            }, "Load mods from profile");
        }

        private void LoadProfilesIntoDropdown()
        {
            ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug("Loading profiles into dropdown");
                UpdateProfileComboBox();
            }, "Load profiles into dropdown");
        }

        private ModProfile CreateExportCopy(ModProfile original)
        {
            return ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug($"Creating export copy of profile: {original.Name}");

                var copy = new ModProfile
                {
                    Id = original.Id,
                    Name = original.Name,
                    LastModified = original.LastModified
                };

                if (original.AppliedMods != null)
                {
                    copy.AppliedMods = original.AppliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.IsFromArchive ? null : EnsureRelativePath(m.ModFolderPath),
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.IsFromArchive ? EnsureRelativePath(m.ArchiveSource) : null,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList();
                }
                else
                {
                    copy.AppliedMods = new List<AppliedModSetting>();
                }

                App.LogService.LogDebug($"Created export copy of profile with {copy.AppliedMods.Count} mods, all with relative paths");

                return copy;
            }, "Create export copy of profile");
        }

        private string EnsureRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (!Path.IsPathRooted(path))
                return path;

            return PathUtility.ToRelativePath(path);
        }

        private void UpdateProfileComboBox()
        {
            ErrorHandler.ExecuteSafe(() => {
                try
                {
                    ProfileComboBox.Items.Clear();

                    App.LogService.LogDebug($"Updating profile combobox with {_profiles.Count} profiles");

                    foreach (var profile in _profiles)
                    {
                        if (profile != null)
                        {
                            ProfileComboBox.Items.Add(profile.Name);
                        }
                    }

                    if (_activeProfile != null)
                    {
                        int indexToSelect = -1;

                        for (int i = 0; i < _profiles.Count; i++)
                        {
                            if (_profiles[i] != null && _profiles[i].Id == _activeProfile.Id)
                            {
                                indexToSelect = i;
                                break;
                            }
                        }

                        if (indexToSelect >= 0 && indexToSelect < ProfileComboBox.Items.Count)
                        {
                            ProfileComboBox.SelectedIndex = indexToSelect;
                            App.LogService.LogDebug($"Selected profile at index {indexToSelect}: {_activeProfile.Name}");
                        }
                        else if (ProfileComboBox.Items.Count > 0)
                        {
                            ProfileComboBox.SelectedIndex = 0;
                            App.LogService.LogDebug($"Active profile not found in list, selected first profile");
                        }
                    }
                    else if (ProfileComboBox.Items.Count > 0)
                    {
                        ProfileComboBox.SelectedIndex = 0;
                        App.LogService.LogDebug($"No active profile, selected first profile");
                    }

                    App.LogService.LogDebug($"Profile combobox updated with {_profiles.Count} profiles, selected index: {ProfileComboBox.SelectedIndex}");
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error updating profile ComboBox", ex, ErrorCategory.UI);
                }
            }, "Update profile ComboBox");
        }

        private async Task SaveCurrentModsToActiveProfile()
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                try
                {
                    if (_activeProfile == null || _currentGame == null)
                    {
                        App.LogService.Warning("Cannot save mods - no active profile or game");
                        return;
                    }

                    App.LogService.LogDebug($"SaveCurrentModsToActiveProfile: Saving mods to '{_activeProfile.Name}' (ID: {_activeProfile.Id})");

                    var currentMods = _appliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.IsFromArchive ? null : PathUtility.ToRelativePath(m.ModFolderPath),
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.IsFromArchive ? PathUtility.ToRelativePath(m.ArchiveSource) : null,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList();

                    App.LogService.LogDebug($"Saving {currentMods.Count} mods to active profile");

                    // Log detailed mod information at trace level
                    foreach (var mod in currentMods)
                    {
                        App.LogService.Trace($"  - Mod: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}, Active: {mod.IsActive}");
                    }

                    _activeProfile.AppliedMods = new List<AppliedModSetting>(currentMods);
                    _activeProfile.LastModified = DateTime.Now;

                    string normalizedGameId = GetNormalizedCurrentGameId();

                    using (var perfTracker = new PerformanceTracker("SaveModsToProfile"))
                    {
                        await _profileService.UpdateActiveProfileModsAsync(normalizedGameId, currentMods);

                        await _configService.SaveAppliedModsAsync(normalizedGameId, currentMods);
                        if (normalizedGameId != _currentGame.Id)
                        {
                            await _configService.SaveAppliedModsAsync(_currentGame.Id, currentMods);
                        }
                    }

                    App.LogService.Info($"Successfully saved {currentMods.Count} mods to profile '{_activeProfile.Name}'");
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error saving current mods to profile", ex, ErrorCategory.ProfileManagement);
                }
            }, "Save current mods to active profile");
        }

        private string NormalizeGameId(string gameId)
        {
            return ErrorHandler.ExecuteSafe(() => {
                if (string.IsNullOrEmpty(gameId))
                    return gameId;

                gameId = gameId.Trim();

                int underscoreIndex = gameId.IndexOf('_');
                if (underscoreIndex > 0)
                {
                    return gameId.Substring(0, underscoreIndex);
                }

                return gameId;
            }, "Normalize game ID", false);
        }

        private string GetNormalizedCurrentGameId()
        {
            return ErrorHandler.ExecuteSafe(() => {
                if (_currentGame == null)
                    return null;

                string normalized = NormalizeGameId(_currentGame.Id);
                App.LogService.LogDebug($"Normalized game ID from {_currentGame.Id} to {normalized}");
                return normalized;
            }, "Get normalized game ID", false);
        }

        private async Task ApplyF1ManagerModsAsync(GameInfo game, List<ModInfo> activeMods)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("ApplyF1ManagerMods");

                App.LogService.Info($"Applying F1 Manager mods for game: {game.Name}");

                string paksDirectory = FindF1ManagerPaksDirectory(game);
                App.LogService.LogDebug($"Found paks directory: {paksDirectory}");

                FlowTracker.StepFlow("ApplyF1ManagerMods", "CleanOldMods");
                await CleanF1ManagerModsAsync(paksDirectory);

                if (activeMods == null || activeMods.Count == 0)
                {
                    App.LogService.Info("No active mods to apply");
                    FlowTracker.StepFlow("ApplyF1ManagerMods", "NoModsToApply");
                    return;
                }

                FlowTracker.StepFlow("ApplyF1ManagerMods", "ApplyMods");
                int priority = 1;

                var orderedMods = activeMods.OrderByDescending(m => _appliedMods.IndexOf(m)).ToList();
                App.LogService.Info($"Applying {orderedMods.Count} F1 Manager mods in order");

                for (int i = 0; i < orderedMods.Count; i++)
                {
                    var mod = orderedMods[i];
                    App.LogService.LogDebug($"Applying mod {i + 1}/{orderedMods.Count}: {mod.Name} (priority: {priority})");

                    await ApplyF1ManagerModAsync(mod, paksDirectory, priority);
                    priority++;
                }

                App.LogService.Info("F1 Manager mods applied successfully");
                FlowTracker.StepFlow("ApplyF1ManagerMods", "Complete");
                FlowTracker.EndFlow("ApplyF1ManagerMods");
            }, "Apply F1 Manager mods");
        }

        private string FindF1ManagerPaksDirectory(GameInfo game)
        {
            return ErrorHandler.ExecuteSafe(() => {
                App.LogService.LogDebug($"Finding paks directory for F1 Manager game: {game.Name}");

                var possiblePaths = new List<string>();

                if (game.Name.Contains("2024") || game.Name.Contains("24"))
                {
                    possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager24", "Content", "Paks"));
                }
                else if (game.Name.Contains("2023") || game.Name.Contains("23"))
                {
                    possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager23", "Content", "Paks"));
                }
                else if (game.Name.Contains("2022") || game.Name.Contains("22"))
                {
                    possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager22", "Content", "Paks"));
                }

                // Fallback paths
                possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager24", "Content", "Paks"));
                possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager23", "Content", "Paks"));
                possiblePaths.Add(Path.Combine(game.InstallDirectory, "F1Manager22", "Content", "Paks"));

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        App.LogService.LogDebug($"Found paks directory: {path}");
                        return path;
                    }
                }

                App.LogService.Error("Paks directory not found for F1 Manager game");
                throw new DirectoryNotFoundException($"Paks directory not found. Please make sure the game is installed correctly.");
            }, "Find F1 Manager paks directory");
        }

        private async Task CleanF1ManagerModsAsync(string paksDirectory)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                App.LogService.LogDebug($"Cleaning existing F1 Manager mods from: {paksDirectory}");

                var filesToDelete = new List<string>();

                foreach (var file in Directory.GetFiles(paksDirectory, "pakchunk*-???-AMO-*.pak"))
                {
                    filesToDelete.Add(file);
                    App.LogService.Trace($"Marked mod pak for deletion: {Path.GetFileName(file)}");

                    string basePath = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file));

                    if (File.Exists(basePath + ".ucas"))
                        filesToDelete.Add(basePath + ".ucas");

                    if (File.Exists(basePath + ".utoc"))
                        filesToDelete.Add(basePath + ".utoc");
                }

                int deletedCount = 0;

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        App.LogService.Error($"Error deleting file {file}: {ex.Message}");
                    }
                }

                App.LogService.LogDebug($"Deleted {deletedCount} F1 Manager mod files");

                // Ensure any async file operations have time to complete
                if (filesToDelete.Count > 0)
                {
                    await Task.Delay(100);
                }
            }, "Clean F1 Manager mods");
        }

        private async Task ApplyF1ManagerModAsync(ModInfo mod, string paksDirectory, int priority)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                App.LogService.LogDebug($"Applying F1 Manager mod: {mod.Name} (priority: {priority})");

                if (mod.IsFromArchive)
                {
                    await ApplyF1ManagerModFromArchiveAsync(mod, paksDirectory, priority);
                }
                else
                {
                    await ApplyF1ManagerModFromFolderAsync(mod, paksDirectory, priority);
                }
            }, "Apply F1 Manager mod");
        }

        private async Task ApplyF1ManagerModFromFolderAsync(ModInfo mod, string paksDirectory, int priority)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                App.LogService.LogDebug($"Applying F1 Manager mod from folder: {mod.ModFilesPath}");

                await Task.Run(() => {
                    string modFilesPath = mod.ModFilesPath;
                    if (string.IsNullOrEmpty(modFilesPath) || !Directory.Exists(modFilesPath))
                    {
                        App.LogService.Warning($"Mod files path is invalid: {modFilesPath}");
                        return;
                    }

                    int filesCopied = 0;

                    foreach (var pakFile in Directory.GetFiles(modFilesPath, "pakchunk*.pak", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileName(pakFile);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(pakFile);

                        App.LogService.Trace($"Processing pak file: {fileName}");

                        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"pakchunk(\d+)(?:-(\d+))?-(.+)\.pak");

                        if (match.Success)
                        {
                            // Original code extracted chunk number: string chunkNumber = match.Groups[1].Value;
                            string modName = match.Groups[3].Value;
                            string priorityStr = priority.ToString("D3");

                            // Always use pakchunk5 instead of the original chunk number
                            string newFileName = $"pakchunk5-{priorityStr}-AMO-{modName}.pak";
                            string destPath = Path.Combine(paksDirectory, newFileName);

                            App.LogService.Trace($"Copying to: {newFileName}");

                            File.Copy(pakFile, destPath, true);
                            filesCopied++;

                            string basePakPath = Path.Combine(Path.GetDirectoryName(pakFile), fileNameWithoutExt);
                            string baseDestPath = Path.Combine(paksDirectory, Path.GetFileNameWithoutExtension(newFileName));

                            if (File.Exists(basePakPath + ".ucas"))
                            {
                                File.Copy(basePakPath + ".ucas", baseDestPath + ".ucas", true);
                                filesCopied++;
                            }

                            if (File.Exists(basePakPath + ".utoc"))
                            {
                                File.Copy(basePakPath + ".utoc", baseDestPath + ".utoc", true);
                                filesCopied++;
                            }
                        }
                        else if (fileName.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase))
                        {
                            // For files without a regex match but still pakchunk files
                            int dashIndex = fileName.IndexOf('-');
                            // Original code extracted chunk number from original file
                            // string chunkNumber = dashIndex > 0
                            //     ? fileName.Substring(8, dashIndex - 8)
                            //     : fileName.Substring(8, fileName.Length - 8 - 4);

                            string modName = mod.Name.Replace(' ', '_');
                            string priorityStr = priority.ToString("D3");

                            // Always use pakchunk5
                            string newFileName = $"pakchunk5-{priorityStr}-AMO-{modName}.pak";
                            string destPath = Path.Combine(paksDirectory, newFileName);

                            App.LogService.Trace($"Copying to: {newFileName}");

                            File.Copy(pakFile, destPath, true);
                            filesCopied++;

                            string basePakPath = Path.Combine(Path.GetDirectoryName(pakFile), fileNameWithoutExt);
                            string baseDestPath = Path.Combine(paksDirectory, Path.GetFileNameWithoutExtension(newFileName));

                            if (File.Exists(basePakPath + ".ucas"))
                            {
                                File.Copy(basePakPath + ".ucas", baseDestPath + ".ucas", true);
                                filesCopied++;
                            }

                            if (File.Exists(basePakPath + ".utoc"))
                            {
                                File.Copy(basePakPath + ".utoc", baseDestPath + ".utoc", true);
                                filesCopied++;
                            }
                        }
                    }

                    App.LogService.LogDebug($"Applied folder mod with {filesCopied} files copied");
                });
            }, "Apply F1 Manager mod from folder");
        }

        private async Task ApplyF1ManagerModFromArchiveAsync(ModInfo mod, string paksDirectory, int priority)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                if (string.IsNullOrEmpty(mod.ArchiveSource) || !File.Exists(mod.ArchiveSource))
                {
                    App.LogService.Warning($"Invalid archive source: {mod.ArchiveSource}");
                    return;
                }

                App.LogService.LogDebug($"Applying F1 Manager mod from archive: {mod.ArchiveSource}");

                using (var perfTracker = new PerformanceTracker("ApplyF1ManagerArchiveMod"))
                {
                    using (var archive = SharpCompress.Archives.ArchiveFactory.Open(mod.ArchiveSource))
                    {
                        int filesCopied = 0;

                        foreach (var entry in archive.Entries)
                        {
                            if (entry.IsDirectory) continue;

                            string entryKey = entry.Key.Replace('\\', '/');
                            string fileName = Path.GetFileName(entryKey);
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(entryKey);

                            App.LogService.Trace($"Processing archive entry: {entryKey}");

                            if (fileName.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase) &&
                                Path.GetExtension(entryKey).Equals(".pak", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"pakchunk(\d+)(?:-(\d+))?-(.+)\.pak");

                                if (match.Success)
                                {
                                    // Original code extracted chunk number: string chunkNumber = match.Groups[1].Value;
                                    string modName = match.Groups[3].Value;
                                    string priorityStr = priority.ToString("D3");

                                    // Always use pakchunk5 instead of the original chunk number
                                    string newFileName = $"pakchunk5-{priorityStr}-AMO-{modName}.pak";
                                    string destPath = Path.Combine(paksDirectory, newFileName);

                                    App.LogService.Trace($"Extracting to: {newFileName}");

                                    using (var entryStream = entry.OpenEntryStream())
                                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                                    {
                                        await entryStream.CopyToAsync(fileStream);
                                        filesCopied++;
                                    }

                                    string baseEntryName = fileNameWithoutExt;
                                    string baseDestName = Path.GetFileNameWithoutExtension(newFileName);

                                    var ucasEntry = archive.Entries.FirstOrDefault(e =>
                                        Path.GetFileNameWithoutExtension(e.Key.Replace('\\', '/')) == baseEntryName &&
                                        Path.GetExtension(e.Key).Equals(".ucas", StringComparison.OrdinalIgnoreCase));

                                    var utocEntry = archive.Entries.FirstOrDefault(e =>
                                        Path.GetFileNameWithoutExtension(e.Key.Replace('\\', '/')) == baseEntryName &&
                                        Path.GetExtension(e.Key).Equals(".utoc", StringComparison.OrdinalIgnoreCase));

                                    if (ucasEntry != null)
                                    {
                                        string ucasDestPath = Path.Combine(paksDirectory, baseDestName + ".ucas");
                                        using (var entryStream = ucasEntry.OpenEntryStream())
                                        using (var fileStream = new FileStream(ucasDestPath, FileMode.Create, FileAccess.Write))
                                        {
                                            await entryStream.CopyToAsync(fileStream);
                                            filesCopied++;
                                        }
                                    }

                                    if (utocEntry != null)
                                    {
                                        string utocDestPath = Path.Combine(paksDirectory, baseDestName + ".utoc");
                                        using (var entryStream = utocEntry.OpenEntryStream())
                                        using (var fileStream = new FileStream(utocDestPath, FileMode.Create, FileAccess.Write))
                                        {
                                            await entryStream.CopyToAsync(fileStream);
                                            filesCopied++;
                                        }
                                    }
                                }
                                else if (fileName.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase))
                                {
                                    // For files without a regex match but still pakchunk files
                                    int dashIndex = fileName.IndexOf('-');
                                    // Original code extracted chunk number from original file
                                    // string chunkNumber = dashIndex > 0
                                    //     ? fileName.Substring(8, dashIndex - 8)
                                    //     : fileName.Substring(8, fileName.Length - 8 - 4);

                                    string modName = mod.Name.Replace(' ', '_');
                                    string priorityStr = priority.ToString("D3");

                                    // Always use pakchunk5
                                    string newFileName = $"pakchunk5-{priorityStr}-AMO-{modName}.pak";
                                    string destPath = Path.Combine(paksDirectory, newFileName);

                                    App.LogService.Trace($"Extracting to: {newFileName}");

                                    using (var entryStream = entry.OpenEntryStream())
                                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                                    {
                                        await entryStream.CopyToAsync(fileStream);
                                        filesCopied++;
                                    }

                                    string baseEntryName = fileNameWithoutExt;
                                    string baseDestName = Path.GetFileNameWithoutExtension(newFileName);

                                    var ucasEntry = archive.Entries.FirstOrDefault(e =>
                                        Path.GetFileNameWithoutExtension(e.Key.Replace('\\', '/')) == baseEntryName &&
                                        Path.GetExtension(e.Key).Equals(".ucas", StringComparison.OrdinalIgnoreCase));

                                    var utocEntry = archive.Entries.FirstOrDefault(e =>
                                        Path.GetFileNameWithoutExtension(e.Key.Replace('\\', '/')) == baseEntryName &&
                                        Path.GetExtension(e.Key).Equals(".utoc", StringComparison.OrdinalIgnoreCase));

                                    if (ucasEntry != null)
                                    {
                                        string ucasDestPath = Path.Combine(paksDirectory, baseDestName + ".ucas");
                                        using (var entryStream = ucasEntry.OpenEntryStream())
                                        using (var fileStream = new FileStream(ucasDestPath, FileMode.Create, FileAccess.Write))
                                        {
                                            await entryStream.CopyToAsync(fileStream);
                                            filesCopied++;
                                        }
                                    }

                                    if (utocEntry != null)
                                    {
                                        string utocDestPath = Path.Combine(paksDirectory, baseDestName + ".utoc");
                                        using (var entryStream = utocEntry.OpenEntryStream())
                                        using (var fileStream = new FileStream(utocDestPath, FileMode.Create, FileAccess.Write))
                                        {
                                            await entryStream.CopyToAsync(fileStream);
                                            filesCopied++;
                                        }
                                    }
                                }
                            }
                        }

                        App.LogService.LogDebug($"Applied archive mod with {filesCopied} files extracted");
                    }
                }
            }, "Apply F1 Manager mod from archive");
        }

        private async void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () => {
                FlowTracker.StartFlow("ApplyModChanges");

                App.LogService.Info("Apply changes button clicked");

                if (_currentGame == null)
                {
                    App.LogService.Warning("Cannot apply changes - no game selected");
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    FlowTracker.StepFlow("ApplyModChanges", "NoGame");
                    return;
                }

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var currentActiveMods = _appliedMods.Where(m => m.IsActive)
                        .Select(m => new AppliedModSetting
                        {
                            ModFolderPath = m.ModFolderPath,
                            IsActive = true,
                            IsFromArchive = m.IsFromArchive,
                            ArchiveSource = m.ArchiveSource,
                            ArchiveRootPath = m.ArchiveRootPath
                        })
                        .ToList();

                    FlowTracker.StepFlow("ApplyModChanges", "CheckChanges");
                    bool modsChanged = _configService.HaveModsChanged(_currentGame.Id, currentActiveMods);

                    if (!modsChanged)
                    {
                        App.LogService.Info("No changes detected to apply");
                        MessageBox.Show("No changes detected to apply.", "No Changes",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        FlowTracker.StepFlow("ApplyModChanges", "NoChanges");
                        return;
                    }

                    FlowTracker.StepFlow("ApplyModChanges", "ShowProgress");
                    var progressWindow = new Views.BackupProgressWindow();
                    progressWindow.Owner = this;
                    progressWindow.SetGame(_currentGame);
                    progressWindow.SetOperationType("Applying Mod Changes");
                    Application.Current.Dispatcher.Invoke(() => { progressWindow.Show(); });

                    try
                    {
                        bool hasActiveMods = currentActiveMods.Count > 0;
                        bool isF1ManagerGame = _currentGame.Name.Contains("Manager");

                        progressWindow.UpdateProgress(0.1, hasActiveMods ?
                            "Preparing to apply mods..." :
                            "Preparing to remove mods...");

                        if (isF1ManagerGame)
                        {
                            FlowTracker.StepFlow("ApplyModChanges", "F1ManagerMods");
                            progressWindow.UpdateProgress(0.3, hasActiveMods ?
                                "Applying F1 Manager mods..." :
                                "Removing F1 Manager mods...");

                            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

                            try
                            {
                                App.LogService.Info("Processing F1 Manager mods");
                                string paksDirectory = FindF1ManagerPaksDirectory(_currentGame);
                                await CleanF1ManagerModsAsync(paksDirectory);

                                if (hasActiveMods)
                                {
                                    int priority = 1;
                                    var orderedMods = activeMods.OrderByDescending(m => _appliedMods.IndexOf(m)).ToList();
                                    App.LogService.LogDebug($"Applying {orderedMods.Count} F1 Manager mods");

                                    for (int i = 0; i < orderedMods.Count; i++)
                                    {
                                        var mod = orderedMods[i];
                                        double progress = 0.3 + (i * 0.5 / orderedMods.Count);
                                        progressWindow.UpdateProgress(progress, $"Applying mod ({i + 1}/{orderedMods.Count}): {mod.Name}");

                                        App.LogService.LogDebug($"Applying mod {i + 1}/{orderedMods.Count}: {mod.Name}");
                                        await ApplyF1ManagerModAsync(mod, paksDirectory, priority);
                                        priority++;
                                    }
                                }

                                progressWindow.UpdateProgress(0.9, hasActiveMods ?
                                    "All F1 Manager mods applied successfully!" :
                                    "All F1 Manager mods removed successfully!");

                                App.LogService.Info(hasActiveMods ?
                                    $"Applied {activeMods.Count} F1 Manager mods successfully" :
                                    "Removed all F1 Manager mods successfully");
                            }
                            catch (DirectoryNotFoundException ex)
                            {
                                FlowTracker.StepFlow("ApplyModChanges", "DirectoryError");
                                LogCategorizedError("Error finding F1 Manager directory", ex, ErrorCategory.FileSystem);
                                progressWindow.ShowError($"Error applying F1 Manager mods: {ex.Message}\n\nPlease check if the game is installed correctly.");
                                await Task.Delay(3000);
                                return;
                            }
                        }
                        else
                        {
                            // Standard game mod application
                            string gameInstallDir = _currentGame.InstallDirectory;
                            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

                            if (!Directory.Exists(backupDir))
                            {
                                FlowTracker.StepFlow("ApplyModChanges", "BackupMissing");
                                App.LogService.Error($"Original game backup not found at {backupDir}");
                                progressWindow.ShowError("Original game data backup not found. Please reset your game data from the Settings window.");
                                await Task.Delay(3000);
                                return;
                            }

                            FlowTracker.StepFlow("ApplyModChanges", "RestoreBackup");
                            await Task.Run(() =>
                            {
                                progressWindow.UpdateProgress(0.3, "Restoring original game files...");
                                App.LogService.LogDebug($"Restoring game files from backup in {backupDir}");
                                using (var perfTracker = new PerformanceTracker("RestoreGameFiles"))
                                {
                                    CopyDirectoryContents(backupDir, gameInstallDir);
                                }
                            });

                            if (hasActiveMods)
                            {
                                FlowTracker.StepFlow("ApplyModChanges", "ApplyMods");
                                var activeMods = _appliedMods.Where(m => m.IsActive).ToList();
                                App.LogService.LogDebug($"Applying {activeMods.Count} mods");

                                for (int i = 0; i < activeMods.Count; i++)
                                {
                                    var mod = activeMods[i];
                                    double progress = 0.3 + (i * 0.5 / activeMods.Count);

                                    progressWindow.UpdateProgress(progress, $"Applying mod ({i + 1}/{activeMods.Count}): {mod.Name}");
                                    App.LogService.LogDebug($"Applying mod {i + 1}/{activeMods.Count}: {mod.Name}");

                                    await Task.Run(() =>
                                    {
                                        if (mod.IsFromArchive)
                                        {
                                            App.LogService.LogDebug($"Applying archive mod: {mod.ArchiveSource}");
                                            ApplyModFromArchive(mod, gameInstallDir);
                                        }
                                        else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                                        {
                                            App.LogService.LogDebug($"Applying folder mod: {mod.ModFilesPath}");
                                            CopyDirectoryContents(mod.ModFilesPath, gameInstallDir);
                                        }
                                        else
                                        {
                                            App.LogService.Warning($"Cannot apply mod - invalid path: {mod.ModFilesPath}");
                                        }
                                    });
                                }

                                progressWindow.UpdateProgress(0.9, "All mods applied successfully!");
                                App.LogService.Info($"Applied {activeMods.Count} mods successfully");
                            }
                            else
                            {
                                progressWindow.UpdateProgress(0.9, "Game restored to original state!");
                                App.LogService.Info("Game restored to original state (no active mods)");
                            }
                        }

                        FlowTracker.StepFlow("ApplyModChanges", "SaveState");
                        _configService.SaveLastAppliedModsState(_currentGame.Id, currentActiveMods);
                        _configService.ResetModsChangedFlag();

                        progressWindow.UpdateProgress(1.0, "Changes applied successfully!");
                        await Task.Delay(1000);

                        App.LogService.Info("All changes applied successfully");
                        FlowTracker.StepFlow("ApplyModChanges", "Complete");
                    }
                    catch (Exception ex)
                    {
                        FlowTracker.StepFlow("ApplyModChanges", "Error");
                        LogCategorizedError("Error applying changes", ex, ErrorCategory.ModProcessing);
                        progressWindow.ShowError($"Error applying changes: {ex.Message}");
                        await Task.Delay(3000);
                    }
                    finally
                    {
                        Application.Current.Dispatcher.Invoke(() => { progressWindow.Close(); });
                    }
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error applying changes", ex, ErrorCategory.ModProcessing);
                    MessageBox.Show($"Error applying changes: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    FlowTracker.EndFlow("ApplyModChanges");
                }
            }, "Apply mod changes");
        }

        private void UpdateService_UpdateAvailable(object sender, UpdateAvailableEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() => {
                try
                {
                    App.LogService.Info($"Update available: current={e.CurrentVersion}, new={e.NewVersion}");

                    Dispatcher.Invoke(async () =>
                    {
                        var updateDialog = new Views.UpdateAvailableDialog(e.CurrentVersion, e.NewVersion, e.ReleaseNotes);
                        updateDialog.Owner = this;

                        bool? result = updateDialog.ShowDialog();

                        if (result == true && updateDialog.InstallNow)
                        {
                            App.LogService.Info("User chose to install update now");

                            try
                            {
                                Mouse.OverrideCursor = Cursors.Wait;

                                // Download the update
                                App.LogService.LogDebug($"Downloading update from {e.DownloadUrl}");
                                string updateZipPath = await App.UpdateService.DownloadUpdateAsync(e.DownloadUrl);

                                // Prepare the update files
                                App.LogService.LogDebug("Preparing update files");
                                bool prepared = await App.UpdateService.PrepareUpdateAsync(updateZipPath);

                                if (prepared)
                                {
                                    // Get the extracted path
                                    string extractedPath = Path.Combine(
                                        Path.Combine(
                                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                            "AMO_Launcher"),
                                        "Updates",
                                        "ExtractedUpdate");

                                    // Apply the update - this will launch the updater executable
                                    App.LogService.LogDebug("Launching updater");
                                    bool launched = App.UpdateService.ApplyUpdate(extractedPath);

                                    if (launched)
                                    {
                                        App.LogService.Info("Updater launched successfully, shutting down");
                                        // Close this application so the updater can work
                                        Application.Current.Shutdown();
                                    }
                                    else
                                    {
                                        App.LogService.Error("Failed to launch updater");
                                        MessageBox.Show("Failed to launch the updater. Please try again later.",
                                            "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                                    }
                                }
                                else
                                {
                                    App.LogService.Error("Failed to prepare update files");
                                    MessageBox.Show("Failed to prepare the update files. Please try again later.",
                                        "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                Mouse.OverrideCursor = null;
                                LogCategorizedError("Error during update installation", ex, ErrorCategory.Unknown);
                                MessageBox.Show($"Error during update installation: {ex.Message}",
                                    "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            finally
                            {
                                Mouse.OverrideCursor = null;
                            }
                        }
                        else
                        {
                            App.LogService.LogDebug("User chose not to install update now");
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogCategorizedError("Error handling update available", ex, ErrorCategory.Unknown);
                }
            }, "Handle update available");
        }

        private void UpdateService_UpdateCheckFailed(object sender, Exception e)
        {
            App.LogService.Warning($"Update check failed: {e.Message}");
        }

        private void InitializeTreeViewMultiSelect()
        {
            App.LogService.Info("Initializing TreeView multi-select behavior");

            AvailableModsTreeView.PreviewMouseDown += AvailableModsTreeView_PreviewMouseDown;
            AvailableModsTreeView.PreviewKeyDown += AvailableModsTreeView_PreviewKeyDown;
            AvailableModsTreeView.PreviewKeyUp += AvailableModsTreeView_PreviewKeyUp;

            // We still want to keep track of the main selected item for description panel
            AvailableModsTreeView.SelectedItemChanged += AvailableModsTreeView_SelectedItemChanged;

            // Initialize selected items collection
            _selectedTreeViewItems = new ObservableCollection<object>();
            _selectedTreeViewItems.CollectionChanged += (s, e) =>
            {
                App.LogService.LogDebug($"Selected items count changed: {_selectedTreeViewItems.Count}");
                UpdateSelectionVisuals();
            };
        }

        private void AvailableModsTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                _isCtrlPressed = true;
            }
            else if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _isShiftPressed = true;
            }
        }

        private void AvailableModsTreeView_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                _isCtrlPressed = false;
            }
            else if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _isShiftPressed = false;
            }
        }

        private void AvailableModsTreeView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only handle left mouse button
            if (e.ChangedButton != MouseButton.Left)
                return;

            _isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            _isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            // Find the clicked TreeViewItem
            var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem == null)
                return;

            // Get the data item (ModInfo or ModCategory)
            var clickedItem = treeViewItem.DataContext;

            // If the item is a category, skip the multi-select logic for now (optional)
            if (clickedItem is ModCategory && !_isCtrlPressed && !_isShiftPressed)
                return; // Let default TreeView handling work for categories

            // Handle multi-selection
            if (_isCtrlPressed)
            {
                HandleCtrlSelection(clickedItem);
                e.Handled = true;
            }
            else if (_isShiftPressed)
            {
                HandleShiftSelection(clickedItem);
                e.Handled = true;
            }
            else
            {
                // Standard click - clear previous selection
                _selectedTreeViewItems.Clear();
                _selectedTreeViewItems.Add(clickedItem);
                _lastSelectedItem = clickedItem;

                // Allow the default selection behavior to continue
            }

            // If we handled the event, make sure to update the visuals
            if (e.Handled)
            {
                UpdateSelectionVisuals();
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        private void HandleCtrlSelection(object treeDataItem)
        {
            if (_selectedTreeViewItems.Contains(treeDataItem))
            {
                // Deselect if already selected
                _selectedTreeViewItems.Remove(treeDataItem);
            }
            else
            {
                // Add to selection if not already selected
                _selectedTreeViewItems.Add(treeDataItem);
                _lastSelectedItem = treeDataItem;
            }
        }

        private void HandleShiftSelection(object treeDataItem)
        {
            if (_lastSelectedItem == null)
            {
                _selectedTreeViewItems.Add(treeDataItem);
                _lastSelectedItem = treeDataItem;
                return;
            }

            // Build or refresh the flattened list of all items if needed
            RebuildFlattenedTreeItems();

            // Find the indices of the last selected item and the clicked item
            int lastIndex = _flattenedTreeItems.IndexOf(_lastSelectedItem);
            int clickedIndex = _flattenedTreeItems.IndexOf(treeDataItem);

            if (lastIndex < 0 || clickedIndex < 0)
                return;

            // Clear the current selection
            _selectedTreeViewItems.Clear();

            // Select the range
            int startIndex = Math.Min(lastIndex, clickedIndex);
            int endIndex = Math.Max(lastIndex, clickedIndex);

            for (int i = startIndex; i <= endIndex; i++)
            {
                _selectedTreeViewItems.Add(_flattenedTreeItems[i]);
            }

            // Update the last selected item
            _lastSelectedItem = treeDataItem;
        }

        private void RebuildFlattenedTreeItems()
        {
            _flattenedTreeItems.Clear();

            foreach (var treeCategory in _availableModsCategories)
            {
                // Add the category itself
                _flattenedTreeItems.Add(treeCategory);

                // Add all mods in the category
                foreach (var treeMod in treeCategory.Mods)
                {
                    _flattenedTreeItems.Add(treeMod);
                }
            }
        }

        private void UpdateSelectionVisuals()
        {
            foreach (var item in FindVisualTreeItems<TreeViewItem>(AvailableModsTreeView))
            {
                if (item.DataContext != null)
                {
                    bool isSelected = _selectedTreeViewItems.Contains(item.DataContext);

                    // Apply visual selection style
                    if (isSelected)
                    {
                        item.Background = new SolidColorBrush(Color.FromArgb(50, 253, 154, 105)); // Light orange background
                                                                                                  // Remove the border by setting BorderThickness to 0
                        item.BorderThickness = new Thickness(0);
                    }
                    else
                    {
                        item.ClearValue(TreeViewItem.BackgroundProperty);
                        item.ClearValue(TreeViewItem.BorderBrushProperty);
                        item.BorderThickness = new Thickness(0);
                    }
                }
            }
        }

        private IEnumerable<T> FindVisualTreeItems<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                yield break;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T item)
                    yield return item;

                foreach (var childOfChild in FindVisualTreeItems<T>(child))
                    yield return childOfChild;
            }
        }



        // ===== Enhanced Error Logging and Diagnostics =====

        private void LogCategorizedError(string message, Exception ex, ErrorCategory category)
        {
            try
            {
                // Category prefix for error message
                string categoryPrefix = $"[{category}] ";

                // Basic logging
                App.LogService.Error($"{categoryPrefix}{message}");

                if (ex != null)
                {
                    // Log exception details in debug mode
                    App.LogService.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                    App.LogService.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                    // Special handling for different categories
                    switch (category)
                    {
                        case ErrorCategory.FileSystem:
                            if (ex is IOException || ex is UnauthorizedAccessException)
                            {
                                // Additional file system error details
                                App.LogService.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                            }
                            break;

                        case ErrorCategory.ModProcessing:
                            // For mod errors, check if it's a mod format issue
                            if (ex.Message.Contains("json") || ex.Message.Contains("JSON"))
                            {
                                App.LogService.LogDebug($"{categoryPrefix}Possible mod.json parsing error - invalid format");
                            }
                            else if (ex.Message.Contains("read") || ex.Message.Contains("extract"))
                            {
                                App.LogService.LogDebug($"{categoryPrefix}Possible mod file extraction error");
                            }
                            break;

                        case ErrorCategory.ProfileManagement:
                            if (ex.Message.Contains("not found") || ex.Message.Contains("doesn't exist"))
                            {
                                App.LogService.LogDebug($"{categoryPrefix}Profile data may be corrupted or missing");
                            }
                            break;
                    }

                    // Log inner exception if present
                    if (ex.InnerException != null)
                    {
                        App.LogService.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                        App.LogService.LogDebug($"{categoryPrefix}Inner stack trace: {ex.InnerException.StackTrace}");
                    }
                }
            }
            catch
            {
                // If logging itself fails, fall back to basic error logging
                App.LogError($"Error in {category}: {message}", ex);
            }
        }
    }

    public class PerformanceTracker : IDisposable
    {
        private string _operationName;
        private System.Diagnostics.Stopwatch _stopwatch;
        private LogLevel _logLevel;
        private long _warningThresholdMs;

        public PerformanceTracker(string operationName, LogLevel logLevel = LogLevel.DEBUG, long warningThresholdMs = 1000)
        {
            _operationName = operationName;
            _logLevel = logLevel;
            _warningThresholdMs = warningThresholdMs;

            _stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Log the start
            switch (_logLevel)
            {
                case LogLevel.ERROR:
                    App.LogService.Error($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.WARNING:
                    App.LogService.Warning($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.INFO:
                    App.LogService.Info($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.DEBUG:
                    App.LogService.LogDebug($"[PERF] Starting: {_operationName}");
                    break;
                case LogLevel.TRACE:
                    App.LogService.Trace($"[PERF] Starting: {_operationName}");
                    break;
            }
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            long elapsedMs = _stopwatch.ElapsedMilliseconds;

            // Format elapsed time
            string formattedTime = FormatTimeSpan(TimeSpan.FromMilliseconds(elapsedMs));

            // Determine log level - elevate to WARNING if threshold exceeded
            LogLevel logLevel = _logLevel;
            if (elapsedMs > _warningThresholdMs)
            {
                logLevel = LogLevel.WARNING;
            }

            // Log the completion time
            switch (logLevel)
            {
                case LogLevel.ERROR:
                    App.LogService.Error($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.WARNING:
                    App.LogService.Warning($"[PERF] Completed: {_operationName} took {formattedTime} (exceeded threshold of {_warningThresholdMs}ms)");
                    break;
                case LogLevel.INFO:
                    App.LogService.Info($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.DEBUG:
                    App.LogService.LogDebug($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
                case LogLevel.TRACE:
                    App.LogService.Trace($"[PERF] Completed: {_operationName} took {formattedTime}");
                    break;
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{timeSpan.TotalDays:0.#} days";
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.TotalHours:0.#} hours";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.TotalMinutes:0.#} minutes";
            }
            else if (timeSpan.TotalSeconds >= 1)
            {
                return $"{timeSpan.TotalSeconds:0.##} seconds";
            }
            else
            {
                return $"{timeSpan.TotalMilliseconds:0} ms";
            }
        }
    }

    public static class FlowTracker
    {
        // Track the start of a logical application flow
        public static void StartFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] START");
        }

        // Track the end of a logical application flow
        public static void EndFlow(string flowName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] END");
        }

        // Track a step within a flow
        public static void StepFlow(string flowName, string stepName)
        {
            App.LogService.LogDebug($"[FLOW:{flowName}] STEP: {stepName}");
        }
    }

    public static class ModExtensions
    {
        public static ObservableCollection<ModCategory> GroupByCategory(this IEnumerable<ModInfo> mods)
        {
            var categories = new ObservableCollection<ModCategory>();
            var categoryDict = new Dictionary<string, ModCategory>();

            foreach (var mod in mods)
            {
                string categoryName = string.IsNullOrEmpty(mod.Category) ? "General" : mod.Category;

                if (!categoryDict.TryGetValue(categoryName, out var category))
                {
                    category = new ModCategory { Name = categoryName, Mods = new ObservableCollection<ModInfo>() };
                    categoryDict[categoryName] = category;
                    categories.Add(category);
                }

                category.Mods.Add(mod);
            }

            // Sort categories alphabetically with "General" first
            return new ObservableCollection<ModCategory>(
                categories.OrderBy(c => c.Name == "General" ? 0 : 1)
                         .ThenBy(c => c.Name)
            );
        }
    }
}

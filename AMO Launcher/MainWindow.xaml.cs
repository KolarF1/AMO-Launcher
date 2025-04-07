using AMO_Launcher.Models;
using AMO_Launcher.Services;
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
        // Services (accessed via App static properties)
        private GameDetectionService _gameDetectionService => App.GameDetectionService;
        private ConfigurationService _configService => App.ConfigService;
        private ModDetectionService _modDetectionService => App.ModDetectionService;
        private GameBackupService _gameBackupService => App.GameBackupService;
        private ProfileService _profileService => App.ProfileService;
        private List<ModProfile> _profiles = new List<ModProfile>();
        private ModProfile _activeProfile;

        // Collections for UI
        private ObservableCollection<ModInfo> _availableModsFlat;
        private ObservableCollection<ModCategory> _availableModsCategories;
        private ObservableCollection<ModInfo> _appliedMods;

        // Currently selected game
        private GameInfo _currentGame;

        // Track if mods need to be reapplied
        private bool _appliedModsChanged = false;

        // Window state tracking
        private bool _isMaximized = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeConflictSystem();

            // Initialize collections
            _availableModsFlat = new ObservableCollection<ModInfo>();
            _availableModsCategories = new ObservableCollection<ModCategory>();
            _appliedMods = new ObservableCollection<ModInfo>();

            // Set up the TreeView for mods
            AvailableModsTreeView.ItemsSource = _availableModsCategories;
            AppliedModsListView.ItemsSource = _appliedMods;

            // Set window title with version
            Title = $"AMO Launcher v{GetAppVersion()}";

            // Add window chrome resize handler
            this.SizeChanged += MainWindow_SizeChanged;

            // Initialize buttons visibility based on selected tab
            if (ModTabControl != null)
            {
                ModTabControl.SelectionChanged += TabControl_SelectionChanged;

                // Set initial visibility
                if (ModActionButtons != null)
                {
                    ModActionButtons.Visibility =
                        ModTabControl.SelectedItem == AppliedModsTab
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                }
            }

            // Set up the context menu for the mods list
        }

        // Get application version from assembly
        private string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        // Window loaded event handler
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Show loading indicator
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Load settings
                await _configService.LoadSettingsAsync();

                // Try to find and select a game
                await TrySelectGameAsync();

                // Set up the combo box in the simplest way possible
                if (ProfileComboBox != null)
                {
                    ProfileComboBox.Items.Clear();
                    ProfileComboBox.Items.Add("Default Profile");
                    ProfileComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during startup: {ex.Message}", "Startup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Hide loading indicator
                Mouse.OverrideCursor = null;
            }
        }

        #region Custom Title Bar Handlers

        // Handle window dragging
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double click to maximize/restore
                MaximizeButton_Click(sender, e);
            }
            else
            {
                // Single click to drag
                this.DragMove();
            }
        }

        // Handle minimize button click
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Handle maximize/restore button click
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMaximized)
            {
                // Restore window
                this.WindowState = WindowState.Normal;
                MaximizeButton.Content = "\uE922"; // Maximize icon
                _isMaximized = false;
            }
            else
            {
                // Maximize window
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Content = "\uE923"; // Restore icon
                _isMaximized = true;
            }
        }

        // Handle close button click
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Update the maximize/restore button when window state changes
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                MaximizeButton.Content = "\uE923"; // Restore icon
                _isMaximized = true;
            }
            else
            {
                MaximizeButton.Content = "\uE922"; // Maximize icon
                _isMaximized = false;
            }
        }

        #endregion

        // Try to find and select a game from settings or scan
        private async Task TrySelectGameAsync()
        {
            try
            {
                // First, load settings to get preferred game ID
                var settings = await _configService.LoadSettingsAsync();
                string preferredGameId = _configService.GetPreferredGameId();

                // Check if we have any saved games
                if (settings.Games.Count > 0 && !string.IsNullOrEmpty(preferredGameId))
                {
                    // Try to find the preferred game
                    var gameSetting = settings.Games.FirstOrDefault(g => g.Id == preferredGameId);

                    if (gameSetting != null)
                    {
                        // Create GameInfo from saved setting
                        var gameInfo = new GameInfo
                        {
                            Id = gameSetting.Id,
                            Name = gameSetting.Name,
                            ExecutablePath = gameSetting.ExecutablePath,
                            InstallDirectory = gameSetting.InstallDirectory,
                            IsDefault = gameSetting.IsDefault,
                            // Extract the icon (try-catch just in case)
                            Icon = TryExtractIcon(gameSetting.ExecutablePath)
                        };

                        // Set as current game
                        SetCurrentGame(gameInfo);
                        return;
                    }
                }

                // If we get here, we need to scan for games
                var detectedGames = await _gameDetectionService.ScanForGamesAsync();

                if (detectedGames.Count > 0)
                {
                    // Select the first game or prompt user to choose
                    if (detectedGames.Count == 1)
                    {
                        // Only one game found, use it
                        SetCurrentGame(detectedGames[0]);
                    }
                    else
                    {
                        // Multiple games found, show game selection dialog
                        ShowGameSelectionDialog();
                    }
                }
                else
                {
                    // No games found
                    ShowNoGameSelectedUI();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading game: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowNoGameSelectedUI();
            }
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // Listen for mod selection changes in the TreeView
            AvailableModsTreeView.SelectedItemChanged += AvailableModsTreeView_SelectedItemChanged;

            // Add double-click handler to TreeView
            AvailableModsTreeView.MouseDoubleClick += AvailableModsTreeView_MouseDoubleClick;

            // Add double-click handler to AppliedModsListView for removing mods
            AppliedModsListView.MouseDoubleClick += AppliedModsListView_MouseDoubleClick;
        }

        // Handle double-click on available mods to add to applied mods
        private void AvailableModsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the item that was clicked
            DependencyObject originalSource = (DependencyObject)e.OriginalSource;
            while (originalSource != null && !(originalSource is TreeViewItem))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            // Make sure we clicked on an actual item
            if (originalSource is TreeViewItem)
            {
                // Get the data context of the item
                object item = ((TreeViewItem)originalSource).DataContext;

                // Only add mod if it's a ModInfo, not a category
                if (item is ModInfo selectedMod)
                {
                    // Add the mod to applied mods
                    AddModToApplied(selectedMod);

                    // Switch to the Applied Mods tab
                    ModTabControl.SelectedItem = AppliedModsTab;
                }
            }
        }


        // Add this new event handler
        private void AvailableModsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Only handle mod selection, not category selection
            if (e.NewValue is ModInfo selectedMod)
            {
                // Show or hide the Description tab based on whether a mod is selected
                bool modSelected = selectedMod != null;

                // Set the visibility of the Description tab
                DescriptionTab.Visibility = modSelected ? Visibility.Visible : Visibility.Collapsed;

                // Only handle the case when no mod is selected but the Description tab is selected
                if (!modSelected && ModTabControl.SelectedItem == DescriptionTab)
                {
                    // If no mod is selected but the Description tab is selected, switch to Applied Mods tab
                    ModTabControl.SelectedItem = AppliedModsTab;
                }

                // Check if a mod is selected and update the description text
                if (modSelected)
                {
                    // Check if description is empty or null
                    if (string.IsNullOrWhiteSpace(selectedMod.Description))
                    {
                        // Update the description text to show "No Description"
                        ModDescriptionTextBlock.Text = "No Description";
                    }
                    else
                    {
                        // Ensure we display the actual description for mods that have one
                        ModDescriptionTextBlock.Text = selectedMod.Description;
                    }

                    // Update other description fields
                    ModNameTextBlock.Text = selectedMod.Name;
                    ModAuthorTextBlock.Text = selectedMod.Author;
                    ModVersionTextBlock.Text = selectedMod.Version;
                    ModPathTextBlock.Text = selectedMod.ModFolderPath ?? selectedMod.ArchiveSource;
                    ModHeaderImage.Source = selectedMod.Icon;
                }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Create and show the settings window
            var settingsWindow = new Views.SettingsWindow();
            settingsWindow.Owner = this; // Set the main window as the owner
            settingsWindow.ShowDialog(); // Show as dialog (modal)
        }

        // Change game button click handler
        private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
        {
            ShowGameSelectionDialog();
        }

        // Apply button click handler
        private void ApplyModsButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if a game is selected
            if (_currentGame == null)
            {
                MessageBox.Show("Please select a game first.", "No Game Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get selected mod if it's a ModInfo (not a category)
            var selectedItem = AvailableModsTreeView.SelectedItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select one or more mods to apply.", "No Mods Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Handle selection of a ModInfo
            if (selectedItem is ModInfo selectedMod)
            {
                AddModToApplied(selectedMod);
                ModTabControl.SelectedItem = AppliedModsTab;
                return;
            }

            // Handle selection of a category
            if (selectedItem is ModCategory category)
            {
                // Add all mods in the category
                foreach (var mod in category.Mods)
                {
                    AddModToApplied(mod);
                }

                ModTabControl.SelectedItem = AppliedModsTab;
                return;
            }
        }


        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Show/hide the mod action buttons based on which tab is selected
            if (ModActionButtons != null)
            {
                // Only show buttons when Applied Mods tab is selected (not on Conflicts or Description tabs)
                ModActionButtons.Visibility =
                    ModTabControl.SelectedItem == AppliedModsTab
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // Force the buttons panel to update its visual state
                ModActionButtons.UpdateLayout();
            }

            // Handle "No conflicts" message when Conflicts tab is selected
            if (ModTabControl.SelectedItem == ConflictsTab)
            {
                // Check if we have any conflicts
                bool hasConflicts = false;

                if (ConflictsListView.ItemsSource is IEnumerable<ConflictItem> conflictItems)
                {
                    hasConflicts = conflictItems.Any();
                }

                // Find the parent grid
                var parent = VisualTreeHelper.GetParent(ConflictsListView) as Grid;
                if (parent != null)
                {
                    // Find existing message if it exists
                    var existingMessage = parent.Children.OfType<TextBlock>()
                        .FirstOrDefault(tb => tb.Name == "NoConflictsMessage");

                    // If no conflicts and no message yet, add the message
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

                        // Hide the ListView
                        ConflictsListView.Visibility = Visibility.Collapsed;
                    }
                    // If we have conflicts but also have the message, remove the message
                    else if (hasConflicts && existingMessage != null)
                    {
                        parent.Children.Remove(existingMessage);

                        // Make sure conflicts are visible
                        ConflictsListView.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        // Show the game selection dialog
        private void ShowGameSelectionDialog()
        {
            var gameSelectionWindow = new GameSelectionWindow(_gameDetectionService, _configService);
            gameSelectionWindow.Owner = this;

            if (gameSelectionWindow.ShowDialog() == true)
            {
                var selectedGame = gameSelectionWindow.SelectedGame;
                if (selectedGame != null)
                {
                    SetCurrentGame(selectedGame);
                }
            }
            else if (_currentGame == null)
            {
                // If no game was selected and we don't have a current game
                ShowNoGameSelectedUI();
            }
        }

        // Set the current game and update UI
        private async void SetCurrentGame(GameInfo game)
        {
            _currentGame = game;

            // Update UI
            CurrentGameTextBlock.Text = game.Name;

            // Always show game content panel
            GameContentPanel.Visibility = Visibility.Visible;

            // Reset applied mods changed flag
            _appliedModsChanged = false;

            // Check for Original_GameData backup for non-Manager games
            if (!game.Name.Contains("Manager"))
            {
                bool backupReady = await _gameBackupService.EnsureOriginalGameDataBackupAsync(game);
                if (!backupReady)
                {
                    // User cancelled backup creation
                    MessageBox.Show(
                        "Game backup was not created. Some mod features may be limited or not work correctly.",
                        "Backup Not Created",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // Scan for mods for the selected game
            await ScanForModsAsync();

            InitializeProfiles();
            LoadProfilesIntoDropdown();
        }

        // Scan for mods for the current game
        private async Task ScanForModsAsync()
        {
            if (_currentGame == null) return;

            // Show loading indicator
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Clear current mods list
                _availableModsFlat.Clear();
                _availableModsCategories.Clear();

                // Ensure the TreeView is not visible while loading
                AvailableModsTreeView.Visibility = Visibility.Collapsed;

                // Scan for mods
                var mods = await _modDetectionService.ScanForModsAsync(_currentGame);

                // Add mods to flat collection first
                foreach (var mod in mods)
                {
                    _availableModsFlat.Add(mod);
                }

                // Group mods by category
                var categorizedMods = _availableModsFlat.GroupByCategory();

                // Debug information to verify categories
                System.Diagnostics.Debug.WriteLine($"Found {categorizedMods.Count} categories:");
                foreach (var category in categorizedMods)
                {
                    System.Diagnostics.Debug.WriteLine($"  Category: {category.Name}, Mods: {category.Mods.Count}");
                    foreach (var mod in category.Mods)
                    {
                        System.Diagnostics.Debug.WriteLine($"    - {mod.Name}, Category: {mod.Category}");
                    }
                }

                // Important: Replace the collection instead of modifying it
                _availableModsCategories = categorizedMods;

                // Force the TreeView to refresh completely
                AvailableModsTreeView.ItemsSource = null;
                AvailableModsTreeView.ItemsSource = _availableModsCategories;


                // Update UI based on results
                if (_availableModsFlat.Count > 0)
                {
                    // Show the TreeView if we found mods
                    AvailableModsTreeView.Visibility = Visibility.Visible;
                }

                // Load applied mods after loading available mods
                await LoadAppliedModsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning for mods: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Hide loading indicator
                Mouse.OverrideCursor = null;
            }
        }


        // Load the applied mods list
        private async Task LoadAppliedModsAsync()
        {
            try
            {
                App.LogToFile("Loading applied mods");

                // Clear current applied mods
                _appliedMods.Clear();

                if (_currentGame == null)
                {
                    App.LogToFile("No game selected");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                // If we have a valid active profile with mods, use those
                if (_activeProfile != null && _activeProfile.AppliedMods != null && _activeProfile.AppliedMods.Count > 0)
                {
                    App.LogToFile($"Loading mods from active profile: {_activeProfile.Name}");
                    LoadAppliedModsFromProfile(_activeProfile);
                    return;
                }

                // Fallback to using saved mods from ConfigService for backward compatibility
                App.LogToFile("No mods in active profile, falling back to config service");
                var appliedModSettings = _configService.GetAppliedMods(_currentGame.Id);

                if (appliedModSettings == null || appliedModSettings.Count == 0)
                {
                    App.LogToFile("No applied mods found in config service");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                App.LogToFile($"Found {appliedModSettings.Count} mods in config service");

                // For each saved mod, find the corresponding mod in available mods
                foreach (var setting in appliedModSettings)
                {
                    try
                    {
                        // First try to find mod in available mods
                        var mod = _availableModsFlat.FirstOrDefault(m =>
                            m.ModFolderPath == setting.ModFolderPath ||
                            (m.IsFromArchive && m.ArchiveSource == setting.ArchiveSource));

                        if (mod != null)
                        {
                            // Set properties and add to applied mods
                            mod.IsApplied = true;
                            mod.IsActive = setting.IsActive;
                            _appliedMods.Add(mod);
                            App.LogToFile($"Added mod from config: {mod.Name}");
                        }
                        else if (setting.IsFromArchive && !string.IsNullOrEmpty(setting.ArchiveSource))
                        {
                            App.LogToFile($"Trying to load archive mod: {setting.ArchiveSource}");
                            // Try to load from archive directly
                            var archiveMod = await _modDetectionService.LoadModFromArchivePathAsync(
                                setting.ArchiveSource, _currentGame.Name, setting.ArchiveRootPath);

                            if (archiveMod != null)
                            {
                                archiveMod.IsApplied = true;
                                archiveMod.IsActive = setting.IsActive;
                                _appliedMods.Add(archiveMod);
                                App.LogToFile($"Added archive mod: {archiveMod.Name}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(setting.ModFolderPath))
                        {
                            App.LogToFile($"Trying to load folder mod: {setting.ModFolderPath}");
                            // Try to load from folder directly
                            var folderMod = _modDetectionService.LoadModFromFolderPath(setting.ModFolderPath, _currentGame.Name);

                            if (folderMod != null)
                            {
                                folderMod.IsApplied = true;
                                folderMod.IsActive = setting.IsActive;
                                _appliedMods.Add(folderMod);
                                App.LogToFile($"Added folder mod: {folderMod.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error adding mod: {ex.Message}");
                        // Continue with next mod
                    }
                }

                // Show or hide ListView based on whether we have applied mods
                AppliedModsListView.Visibility = _appliedMods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                // Now update mod priorities
                UpdateModPriorities();
                UpdateModPriorityDisplays();
                DetectModConflicts();

                // Also save these mods to the active profile
                if (_activeProfile != null && _appliedMods.Count > 0)
                {
                    App.LogToFile("Saving applied mods to active profile for future use");
                    await SaveAppliedModsAsync();
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading applied mods: {ex.Message}");
                MessageBox.Show($"Error loading applied mods: {ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Refresh mods button click handler
        private async void RefreshModsButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanForModsAsync();
        }

        // Helper method to safely extract icon
        private BitmapImage TryExtractIcon(string executablePath)
        {
            try
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                    return null;

                // Use a direct method without GameDetectionService
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

                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting icon: {ex.Message}");
                return null;
            }
        }

        // Show UI for when no game is selected
        private void ShowNoGameSelectedUI()
        {
            _currentGame = null;

            // Update UI
            CurrentGameTextBlock.Text = "No game selected";

            // Keep game content panel visible, but could update it to show a message
            GameContentPanel.Visibility = Visibility.Visible;

            // Clear mods list
            _availableModsFlat.Clear();
            AvailableModsTreeView.Visibility = Visibility.Collapsed;

            // Clear applied mods list
            _appliedMods.Clear();
            AppliedModsListView.Visibility = Visibility.Collapsed;
        }

        // Add mod to applied mods collection
        // Update AddModToApplied method
        private void AddModToApplied(ModInfo mod)
        {
            // Skip if null
            if (mod == null) return;

            // Show loading cursor
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Check if mod is already in the applied list
                if (!_appliedMods.Any(m => m.ModFolderPath == mod.ModFolderPath ||
                                          (m.IsFromArchive && m.ArchiveSource == mod.ArchiveSource)))
                {
                    // Mark the mod as applied and active
                    mod.IsApplied = true;
                    mod.IsActive = true;

                    // Add to applied mods collection
                    _appliedMods.Add(mod);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();

                    // Ensure the ListView is visible
                    AppliedModsListView.Visibility = Visibility.Visible;

                    // Mark that changes need to be applied
                    _configService.MarkModsChanged();

                    // Save applied mods list without blocking UI
                    SaveAppliedMods();
                    DetectModConflicts();
                }
            }
            finally
            {
                // Clear loading cursor
                Mouse.OverrideCursor = null;
            }
        }

        // Update ActiveCheckBox_CheckChanged method
        private void ActiveCheckBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            // Mark that changes need to be applied
            _configService.MarkModsChanged();

            // Save applied mods list without blocking UI thread
            SaveAppliedMods();
            DetectModConflicts();
        }

        // Update ModActionButton_Click method (if you want to modify it)
        private void ModActionButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                string action = button.Tag?.ToString() ?? "";

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
                        break;
                }
            }
        }

        // Update RemoveSelectedAppliedMods method
        private void RemoveSelectedAppliedMods()
        {
            // Show loading cursor
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var selectedMods = AppliedModsListView.SelectedItems.Cast<ModInfo>().ToList();

                if (selectedMods.Count == 0)
                {
                    MessageBox.Show("Please select one or more mods to remove.", "No Mods Selected",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var mod in selectedMods)
                {
                    mod.IsApplied = false;
                    _appliedMods.Remove(mod);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                }

                // Hide ListView if no mods left
                if (_appliedMods.Count == 0)
                {
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                }

                // Mark that changes need to be applied
                _configService.MarkModsChanged();

                // Save changes without blocking UI
                SaveAppliedMods();
            }
            finally
            {
                // Clear loading cursor
                Mouse.OverrideCursor = null;
            }
        }

        // Update all Move methods
        private void MoveSelectedModToTop()
        {
            if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
            {
                int currentIndex = _appliedMods.IndexOf(selectedMod);
                if (currentIndex > 0)
                {
                    _appliedMods.Move(currentIndex, 0);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    DetectModConflicts();
                }
            }
        }

        private void MoveSelectedModUp()
        {
            if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
            {
                int currentIndex = _appliedMods.IndexOf(selectedMod);
                if (currentIndex > 0)
                {
                    _appliedMods.Move(currentIndex, currentIndex - 1);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    DetectModConflicts();
                }
            }
        }

        private void MoveSelectedModDown()
        {
            if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
            {
                int currentIndex = _appliedMods.IndexOf(selectedMod);
                if (currentIndex < _appliedMods.Count - 1)
                {
                    _appliedMods.Move(currentIndex, currentIndex + 1);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    DetectModConflicts();
                }
            }
        }

        private void MoveSelectedModToBottom()
        {
            if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
            {
                int currentIndex = _appliedMods.IndexOf(selectedMod);
                if (currentIndex < _appliedMods.Count - 1)
                {
                    _appliedMods.Move(currentIndex, _appliedMods.Count - 1);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    _configService.MarkModsChanged();
                    SaveAppliedMods();
                    DetectModConflicts();
                }
            }
        }

        // Save the current applied mods list asynchronously
        private async Task SaveAppliedModsAsync()
        {
            try
            {
                if (_currentGame == null)
                {
                    App.LogToFile("No game selected, cannot save mods");
                    return;
                }

                // Create a list of applied mod settings
                var appliedModSettings = _appliedMods.Select(m => new AppliedModSetting
                {
                    ModFolderPath = m.ModFolderPath,
                    IsActive = m.IsActive,
                    IsFromArchive = m.IsFromArchive,
                    ArchiveSource = m.ArchiveSource,
                    ArchiveRootPath = m.ArchiveRootPath
                }).ToList();

                // Log what we're saving
                App.LogToFile($"Saving {appliedModSettings.Count} mods for game {_currentGame.Name}");

                // Save to profile service (which will also update the active profile)
                await _profileService.UpdateActiveProfileModsAsync(_currentGame.Id, appliedModSettings);

                App.LogToFile("Mods saved successfully");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving applied mods: {ex.Message}");
            }
        }

        // Non-blocking wrapper method for event handlers
        private void SaveAppliedMods()
        {
            // Run the async method without awaiting it
            Task.Run(async () => await SaveAppliedModsAsync()).ConfigureAwait(false);
        }

        // Launch Game button click handler
        private async void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGame == null)
            {
                MessageBox.Show("Please select a game first.", "No Game Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Show loading cursor
                Mouse.OverrideCursor = Cursors.Wait;

                // Log the operation
                System.Diagnostics.Debug.WriteLine($"Launching game: {_currentGame.Name}");

                // Check if mods need to be applied and apply them
                await CheckAndApplyModsAsync();

                // Launch the game
                System.Diagnostics.Debug.WriteLine($"Starting process: {_currentGame.ExecutablePath}");
                Process.Start(_currentGame.ExecutablePath);

                // Reset the flag if game launches successfully
                _configService.ResetModsChangedFlag();

                // Minimize the launcher
                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching game: {ex.Message}", "Launch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Clear loading cursor
                Mouse.OverrideCursor = null;
            }
        }

        // Check if mods need to be applied and apply them
        private async Task CheckAndApplyModsAsync()
        {
            // Skip for F1 Manager games
            if (_currentGame.Name.Contains("Manager"))
            {
                return;
            }

            try
            {
                // Get current active mods
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

                // Check if mods have changed since last application
                bool modsChanged = _configService.HaveModsChanged(_currentGame.Id, currentActiveMods);

                // If mods haven't changed, skip file operations
                if (!modsChanged)
                {
                    System.Diagnostics.Debug.WriteLine("No changes detected in mods - launching game without file operations");
                    return;
                }

                // Show a progress window for the file operations
                var progressWindow = new Views.BackupProgressWindow();
                progressWindow.Owner = this;
                progressWindow.SetGame(_currentGame);
                progressWindow.SetOperationType("Preparing Game Launch");
                Application.Current.Dispatcher.Invoke(() => { progressWindow.Show(); });

                try
                {
                    // Check if we have any active mods
                    bool hasActiveMods = currentActiveMods.Count > 0;

                    // Initialize progress window with appropriate message
                    progressWindow.UpdateProgress(0, hasActiveMods ?
                        "Preparing to apply mods..." :
                        "Restoring original game files...");

                    string gameInstallDir = _currentGame.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

                    // Check if backup directory exists
                    if (!Directory.Exists(backupDir))
                    {
                        progressWindow.ShowError("Original game data backup not found. Please reset your game data from the Settings window.");
                        await Task.Delay(3000);
                        return;
                    }

                    // Always restore original game files first
                    await Task.Run(() =>
                    {
                        progressWindow.UpdateProgress(0.2, "Restoring original game files...");
                        CopyDirectoryContents(backupDir, gameInstallDir);
                    });

                    // If no active mods, we're done
                    if (!hasActiveMods)
                    {
                        progressWindow.UpdateProgress(0.9, "Game restored to original state and ready to launch!");
                    }
                    else
                    {
                        // Apply each active mod in order
                        var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

                        for (int i = 0; i < activeMods.Count; i++)
                        {
                            var mod = activeMods[i];
                            double progress = 0.2 + (i * 0.7 / activeMods.Count); // Scale from 20% to 90%

                            progressWindow.UpdateProgress(progress, $"Applying mod ({i + 1}/{activeMods.Count}): {mod.Name}");

                            await Task.Run(() =>
                            {
                                if (mod.IsFromArchive)
                                {
                                    ApplyModFromArchive(mod, gameInstallDir);
                                }
                                else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                                {
                                    CopyDirectoryContents(mod.ModFilesPath, gameInstallDir);
                                }
                            });
                        }

                        progressWindow.UpdateProgress(0.95, "All mods applied successfully!");
                    }

                    // Save the last applied mods state and reset the changed flag
                    // Use the async version and wait for it to complete
                    _configService.SaveLastAppliedModsState(_currentGame.Id, currentActiveMods);
                    System.Diagnostics.Debug.WriteLine($"After save, checking if state exists: {_configService.GetLastAppliedModsState(_currentGame.Id) != null}");
                    _configService.ResetModsChangedFlag();

                    // Final message
                    progressWindow.UpdateProgress(1.0, "Ready to launch game!");
                    await Task.Delay(1000); // Brief delay to show completion message
                }
                catch (Exception ex)
                {
                    progressWindow.ShowError($"Error preparing game launch: {ex.Message}");
                    await Task.Delay(3000);
                    throw;
                }
                finally
                {
                    // Close the progress window
                    Application.Current.Dispatcher.Invoke(() => { progressWindow.Close(); });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error preparing game launch: {ex.Message}", "Launch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateModPriorities()
        {
            int count = _appliedMods.Count;
            for (int i = 0; i < count; i++)
            {
                // Set priority where 1 is highest (bottom of list)
                _appliedMods[i].Priority = count - i;
            }
            AppliedModsListView.Items.Refresh();
        }

        // Reset game files to original state with shared progress window
        private async Task ResetToOriginalGameDataWithProgressAsync(Views.BackupProgressWindow progressWindow)
        {
            string gameInstallDir = _currentGame.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

            // Skip for Manager games or if backup doesn't exist
            if (_currentGame.Name.Contains("Manager") || !Directory.Exists(backupDir))
            {
                return;
            }

            // Check if there are active mods
            if (_appliedMods.Any(m => m.IsActive))
            {
                // We have active mods, will copy files later
                return;
            }

            // No active mods, restore original game data
            progressWindow.UpdateProgress(0.2, "Restoring original game files before launch...");

            try
            {
                await Task.Run(() =>
                {
                    // Copy files from backup to game directory
                    CopyDirectoryContents(backupDir, gameInstallDir, progressWindow);
                });

                progressWindow.UpdateProgress(0.9, "Game restored to original state and ready to launch!");
            }
            catch (Exception ex)
            {
                progressWindow.ShowError($"Error restoring original game data: {ex.Message}");
                await Task.Delay(3000); // Longer delay to show error
                throw;
            }
        }

        // Apply mods to the game with shared progress window
        private async Task ApplyModsToGameWithProgressAsync(Views.BackupProgressWindow progressWindow)
        {
            // Get active mods in the correct order
            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

            if (activeMods.Count == 0) return;

            // First, reset to original game data by copying files
            string gameInstallDir = _currentGame.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

            // Skip if backup doesn't exist
            if (!Directory.Exists(backupDir))
            {
                progressWindow.ShowError("Original game data backup not found. Please reset your game data from the Settings window.");
                await Task.Delay(3000);
                throw new Exception("Original game data backup not found");
            }

            try
            {
                // First restore original game files
                progressWindow.UpdateProgress(0.1, "Preparing game for modding...");

                await Task.Run(() =>
                {
                    // Copy files from backup to game directory
                    CopyDirectoryContents(backupDir, gameInstallDir, progressWindow);
                });

                // Apply each mod in order
                for (int i = 0; i < activeMods.Count; i++)
                {
                    var mod = activeMods[i];
                    double progress = 0.2 + (i * 0.7 / activeMods.Count); // Scale from 20% to 90%

                    // Update progress
                    progressWindow.UpdateProgress(progress, $"Applying mod ({i + 1}/{activeMods.Count}): {mod.Name}");

                    await Task.Run(() =>
                    {
                        if (mod.IsFromArchive)
                        {
                            // Apply mod from archive
                            ApplyModFromArchive(mod, gameInstallDir);
                        }
                        else
                        {
                            // Apply mod from folder
                            string modFilesPath = mod.ModFilesPath;
                            if (Directory.Exists(modFilesPath))
                            {
                                CopyDirectoryContents(modFilesPath, gameInstallDir, progressWindow);
                            }
                        }
                    });
                }

                // Complete progress
                progressWindow.UpdateProgress(0.95, "All mods applied successfully!");
            }
            catch (Exception ex)
            {
                progressWindow.ShowError($"Error applying mods: {ex.Message}");
                await Task.Delay(3000); // Longer delay to show error
                throw;
            }
        }

        // Check if the applied mods have changed since last application
        private async Task<bool> HaveModsChangedAsync()
        {
            // Get the last applied mods state
            var lastAppliedState = await Task.Run(() => _configService.GetLastAppliedModsState(_currentGame.Id));

            // If no last state, then mods have changed
            if (lastAppliedState == null || lastAppliedState.Count == 0) return true;

            // Get current active mods
            var currentActiveMods = _appliedMods.Where(m => m.IsActive)
                .Select(m => new AppliedModSetting
                {
                    ModFolderPath = m.ModFolderPath,
                    IsFromArchive = m.IsFromArchive,
                    ArchiveSource = m.ArchiveSource,
                    ArchiveRootPath = m.ArchiveRootPath
                })
                .ToList();

            // If count is different, mods have changed
            if (currentActiveMods.Count != lastAppliedState.Count) return true;

            // Check if each mod in current state matches last state
            for (int i = 0; i < currentActiveMods.Count; i++)
            {
                var currentMod = currentActiveMods[i];
                var lastMod = lastAppliedState[i];

                // If different mod or different order, mods have changed
                if (currentMod.ModFolderPath != lastMod.ModFolderPath ||
                    currentMod.IsFromArchive != lastMod.IsFromArchive ||
                    currentMod.ArchiveSource != lastMod.ArchiveSource ||
                    currentMod.ArchiveRootPath != lastMod.ArchiveRootPath)
                {
                    return true;
                }
            }

            // No differences found
            return false;
        }

        // Check if the applied mods have changed since last application
        private bool HaveModsChanged()
        {
            try
            {
                // Get the last applied mods state
                var lastAppliedState = _configService.GetLastAppliedModsState(_currentGame.Id);

                // If no last state, then mods have changed
                if (lastAppliedState == null || lastAppliedState.Count == 0)
                    return true;

                // Get current active mods
                var currentActiveMods = _appliedMods.Where(m => m.IsActive)
                    .Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.ModFolderPath,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.ArchiveSource,
                        ArchiveRootPath = m.ArchiveRootPath
                    })
                    .ToList();

                // If count is different, mods have changed
                if (currentActiveMods.Count != lastAppliedState.Count)
                    return true;

                // Check if each mod in current state matches last state
                for (int i = 0; i < currentActiveMods.Count; i++)
                {
                    var currentMod = currentActiveMods[i];
                    var lastMod = lastAppliedState[i];

                    // If different mod or different order, mods have changed
                    if (currentMod.ModFolderPath != lastMod.ModFolderPath ||
                        currentMod.IsFromArchive != lastMod.IsFromArchive ||
                        currentMod.ArchiveSource != lastMod.ArchiveSource ||
                        currentMod.ArchiveRootPath != lastMod.ArchiveRootPath)
                    {
                        return true;
                    }
                }

                // No differences found
                return false;
            }
            catch (Exception ex)
            {
                // If any error occurs, assume mods have changed to be safe
                System.Diagnostics.Debug.WriteLine($"Error checking mod changes: {ex.Message}");
                return true;
            }
        }

        // Reset game files to original state
        private async Task ResetToOriginalGameDataAsync()
        {
            string gameInstallDir = _currentGame.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

            // Skip for Manager games or if backup doesn't exist
            if (_currentGame.Name.Contains("Manager") || !Directory.Exists(backupDir))
            {
                return;
            }

            // Check if there are active mods
            if (_appliedMods.Any(m => m.IsActive))
            {
                // We have active mods, will copy files later
                return;
            }

            // No active mods, restore original game data

            // Show progress dialog
            var progressWindow = new Views.BackupProgressWindow();
            progressWindow.Owner = this;
            progressWindow.SetGame(_currentGame);
            progressWindow.Show();
            progressWindow.UpdateProgress(0, "Restoring original game files...");

            try
            {
                await Task.Run(() =>
                {
                    // Copy files from backup to game directory
                    CopyDirectoryContents(backupDir, gameInstallDir, progressWindow);
                });

                progressWindow.UpdateProgress(1.0, "Game restored to original state!");
                await Task.Delay(1000); // Brief delay to show completion
            }
            catch (Exception ex)
            {
                progressWindow.ShowError($"Error restoring original game data: {ex.Message}");
                await Task.Delay(3000); // Longer delay to show error
                throw;
            }
            finally
            {
                // Close progress dialog
                progressWindow.Close();
            }
        }

        // Apply mods to the game
        private async Task ApplyModsToGameAsync()
        {
            // Get active mods in the correct order
            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

            if (activeMods.Count == 0) return;

            // First, reset to original game data by copying files
            string gameInstallDir = _currentGame.InstallDirectory;
            string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

            // Skip if backup doesn't exist
            if (!Directory.Exists(backupDir))
            {
                MessageBox.Show("Original game data backup not found. Please reset your game data from the Settings window.",
                    "Backup Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show progress dialog
            var progressWindow = new Views.BackupProgressWindow();
            progressWindow.Owner = this;
            progressWindow.SetGame(_currentGame);
            progressWindow.Show();

            try
            {
                // First restore original game files
                progressWindow.UpdateProgress(0, "Restoring original game files...");

                await Task.Run(() =>
                {
                    // Copy files from backup to game directory
                    CopyDirectoryContents(backupDir, gameInstallDir, progressWindow);
                });

                // Apply each mod in order
                for (int i = 0; i < activeMods.Count; i++)
                {
                    var mod = activeMods[i];
                    double progress = (i * 0.8 / activeMods.Count) + 0.2; // Scale from 20% to 100%

                    // Update progress
                    progressWindow.UpdateProgress(progress, $"Applying mod: {mod.Name}");

                    await Task.Run(() =>
                    {
                        if (mod.IsFromArchive)
                        {
                            // Apply mod from archive
                            ApplyModFromArchive(mod, gameInstallDir);
                        }
                        else
                        {
                            // Apply mod from folder
                            string modFilesPath = mod.ModFilesPath;
                            if (Directory.Exists(modFilesPath))
                            {
                                CopyDirectoryContents(modFilesPath, gameInstallDir, progressWindow);
                            }
                        }
                    });
                }

                // Complete progress
                progressWindow.UpdateProgress(1.0, "Mods applied successfully!");
                await Task.Delay(1000); // Brief delay to show completion
            }
            catch (Exception ex)
            {
                progressWindow.ShowError($"Error applying mods: {ex.Message}");
                await Task.Delay(3000); // Longer delay to show error
                throw;
            }
            finally
            {
                // Close progress dialog
                progressWindow.Close();
            }
        }

        // Copy directory contents recursively
        private void CopyDirectoryContents(string sourceDir, string targetDir, Views.BackupProgressWindow progressWindow = null)
        {
            // Create target directory if it doesn't exist
            Directory.CreateDirectory(targetDir);

            // Copy files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Copy subdirectories
            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(directory);
                string destDir = Path.Combine(targetDir, dirName);
                CopyDirectoryContents(directory, destDir, progressWindow);
            }
        }

        // Apply mod from archive
        private void ApplyModFromArchive(ModInfo mod, string targetDir)
        {
            // Skip if no archive source
            if (string.IsNullOrEmpty(mod.ArchiveSource) || !File.Exists(mod.ArchiveSource))
            {
                return;
            }

            using (var archive = SharpCompress.Archives.ArchiveFactory.Open(mod.ArchiveSource))
            {
                // Get the mod files path within the archive
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

                // Extract files to target directory
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    // Check if this entry is in the Mod folder
                    string entryKey = entry.Key.Replace('\\', '/');
                    if (entryKey.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get the relative path within the Mod folder
                        string relativePath = entryKey.Substring(modPath.Length + 1);
                        string targetPath = Path.Combine(targetDir, relativePath);

                        // Create directory if needed
                        string targetDirPath = Path.GetDirectoryName(targetPath);
                        if (!Directory.Exists(targetDirPath))
                        {
                            Directory.CreateDirectory(targetDirPath);
                        }

                        // Extract the file
                        using (var entryStream = entry.OpenEntryStream())
                        using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                        {
                            entryStream.CopyTo(fileStream);
                        }
                    }
                }
            }
        }

        // Save the last applied mods state
        private void SaveLastAppliedModsState()
        {
            if (_currentGame == null) return;

            var activeMods = _appliedMods.Where(m => m.IsActive)
                .Select(m => new AppliedModSetting
                {
                    ModFolderPath = m.ModFolderPath,
                    IsActive = true,
                    IsFromArchive = m.IsFromArchive,
                    ArchiveSource = m.ArchiveSource,
                    ArchiveRootPath = m.ArchiveRootPath
                })
                .ToList();

            _configService.SaveLastAppliedModsState(_currentGame.Id, activeMods);
        }

        // Save the last applied mods state
        private async Task SaveLastAppliedModsStateAsync()
        {
            if (_currentGame == null) return;

            try
            {
                var activeMods = _appliedMods.Where(m => m.IsActive)
                    .Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.ModFolderPath,
                        IsActive = true,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.ArchiveSource,
                        ArchiveRootPath = m.ArchiveRootPath
                    })
                    .ToList();

                await Task.Run(() => _configService.SaveLastAppliedModsState(_currentGame.Id, activeMods));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving last applied mods state: {ex.Message}");
            }
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

        private async Task EnsureGameBackupAsync(GameInfo game)
        {
            // Skip for Manager games
            if (game?.Name?.Contains("Manager") == true)
            {
                return;
            }

            // Check for Original_GameData backup
            bool backupReady = await _gameBackupService.EnsureOriginalGameDataBackupAsync(game);
            if (!backupReady)
            {
                // User cancelled backup creation
                MessageBox.Show(
                    "Game backup was not created. Some mod features may be limited or not work correctly.",
                    "Backup Not Created",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public GameInfo GetCurrentGame()
        {
            return _currentGame;
        }

        private void UpdateModPriorityDisplays()
        {
            int count = _appliedMods.Count;
            for (int i = 0; i < count; i++)
            {
                // Set priority where 1 is highest (bottom of list)
                _appliedMods[i].PriorityDisplay = (count - i).ToString();
            }
        }

        // Handle delete mod context menu click
        private void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            // Determine which control the context menu was opened from
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu == null) return;

            var treeView = contextMenu.PlacementTarget as TreeView;
            if (treeView == null) return;

            // Get the selected item (could be category or mod)
            var selectedItem = treeView.SelectedItem;
            if (selectedItem == null) return;

            // Handle deletion based on selection type
            if (selectedItem is ModInfo selectedMod)
            {
                // Handle mod deletion
                DeleteModItem(selectedMod);
            }
            else if (selectedItem is ModCategory category)
            {
                // Confirm deletion of entire category
                var result = MessageBox.Show(
                    $"Are you sure you want to delete ALL mods in the '{category.Name}' category?\n\nThis action cannot be undone.",
                    "Confirm Category Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                // Create a copy of the mods list to avoid modification issues
                var modsToDelete = category.Mods.ToList();

                // Delete each mod in the category
                foreach (var mod in modsToDelete)
                {
                    DeleteModItem(mod);
                }
            }
        }

        // Deactivate all mods context menu click handler
        private void DeactivateAllMods_Click(object sender, RoutedEventArgs e)
        {
            if (_appliedMods.Count == 0)
            {
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

            // Deactivate all mods
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Set all mods to inactive
                foreach (var mod in _appliedMods)
                {
                    mod.IsActive = false;
                }

                // Mark changes for saving
                _configService.MarkModsChanged();
                SaveAppliedMods();

                // Removed the message box that said "All mods have been deactivated."
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }


        // Helper method to actually delete the mod files
        private void DeleteMods(List<ModInfo> mods)
        {
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                foreach (var mod in mods)
                {
                    try
                    {
                        // Check if the mod is from an archive
                        if (mod.IsFromArchive)
                        {
                            // Delete the archive file
                            if (!string.IsNullOrEmpty(mod.ArchiveSource) && File.Exists(mod.ArchiveSource))
                            {
                                File.Delete(mod.ArchiveSource);
                            }
                        }
                        else
                        {
                            // Delete the mod folder
                            if (!string.IsNullOrEmpty(mod.ModFolderPath) && Directory.Exists(mod.ModFolderPath))
                            {
                                Directory.Delete(mod.ModFolderPath, true);
                            }
                        }

                        // Remove the mod from the available mods list
                        _availableModsFlat.Remove(mod);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting mod '{mod.Name}': {ex.Message}", "Delete Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Update UI if all available mods are deleted
                if (_availableModsFlat.Count == 0)
                {
                    AvailableModsTreeView.Visibility = Visibility.Collapsed;
                }

                // Also hide applied mods list if empty
                if (_appliedMods.Count == 0)
                {
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                }

                // Mark that changes need to be applied
                _configService.MarkModsChanged();
                SaveAppliedMods();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Handle double-click on applied mods to remove them from the list
        private void AppliedModsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Get the item that was clicked
            DependencyObject originalSource = (DependencyObject)e.OriginalSource;
            while (originalSource != null && !(originalSource is ListViewItem))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            // Make sure we clicked on an actual item
            if (originalSource is ListViewItem)
            {
                // Get the mod that was clicked
                if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
                {
                    // Remove the mod from applied mods without confirmation
                    selectedMod.IsApplied = false;
                    _appliedMods.Remove(selectedMod);

                    // Update priorities and UI
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();

                    // Hide ListView if no mods left
                    if (_appliedMods.Count == 0)
                    {
                        AppliedModsListView.Visibility = Visibility.Collapsed;
                    }

                    // Mark that changes need to be applied
                    _configService.MarkModsChanged();

                    // Save changes
                    SaveAppliedMods();
                }
            }
        }

        private List<ModFileConflict> _modConflicts = new List<ModFileConflict>();

        // Detects conflicts between active mods
        private void DetectModConflicts()
        {
            _modConflicts.Clear();

            // Get all active mods
            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

            // If there are less than 2 active mods, there can't be any conflicts
            if (activeMods.Count < 2)
            {
                UpdateConflictsListView();
                return;
            }

            // Dictionary to track which mods modify which files
            var fileToModsMap = new Dictionary<string, List<ModInfo>>();

            foreach (var mod in activeMods)
            {
                // Get all files this mod modifies
                var modFiles = GetModFiles(mod);

                foreach (var file in modFiles)
                {
                    if (!fileToModsMap.ContainsKey(file))
                    {
                        fileToModsMap[file] = new List<ModInfo>();
                    }
                    fileToModsMap[file].Add(mod);
                }
            }

            // Find conflicts (files modified by more than one mod)
            foreach (var entry in fileToModsMap)
            {
                if (entry.Value.Count > 1)
                {
                    _modConflicts.Add(new ModFileConflict
                    {
                        FilePath = entry.Key,
                        ConflictingMods = entry.Value.ToList()
                    });
                }
            }

            // Update the UI
            UpdateConflictsListView();

            // Highlight winning mods
            HighlightWinningMods();
        }

        // Updates the conflicts list view with current conflicts
        private void UpdateConflictsListView()
        {
            // Create a list of conflict items for the UI
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

            // Sort by file path and then by mod name for clearer grouping
            conflictItems = conflictItems.OrderBy(c => c.FilePath).ThenBy(c => c.ModName).ToList();

            // Update the ConflictsListView
            ConflictsListView.ItemsSource = conflictItems;

            // Remove "No conflicts" message if it exists and we have conflicts
            if (conflictItems.Count > 0)
            {
                // Find and remove the "No conflicts" message if it exists
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

                // Make sure ListView is visible
                ConflictsListView.Visibility = Visibility.Visible;
            }
            else
            {
                // Only hide the ListView if no conflicts were found and we're showing the message
                var parent = VisualTreeHelper.GetParent(ConflictsListView) as Grid;
                bool messageExists = false;

                if (parent != null)
                {
                    messageExists = parent.Children.OfType<TextBlock>()
                        .Any(tb => tb.Name == "NoConflictsMessage");
                }

                // Only hide if we're showing the message
                if (messageExists)
                {
                    ConflictsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ConflictsListView.Visibility = Visibility.Visible;
                }
            }

            // Update badge count on Conflicts tab
            UpdateConflictTabBadge(conflictItems.Count);
        }

        // Get files modified by a mod
        private List<string> GetModFiles(ModInfo mod)
        {
            var files = new List<string>();

            try
            {
                if (mod.IsFromArchive)
                {
                    // Extract file list from archive
                    files = GetFilesFromArchive(mod.ArchiveSource, mod.ArchiveRootPath);
                }
                else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                {
                    // Get files from folder
                    files = GetFilesFromFolder(mod.ModFilesPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting files for mod {mod.Name}: {ex.Message}");
            }

            return files;
        }

        // Get files from a mod archive
        private List<string> GetFilesFromArchive(string archivePath, string rootPath)
        {
            var files = new List<string>();

            if (!File.Exists(archivePath)) return files;

            try
            {
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
                            // Get the relative path within the Mod folder
                            string relativePath = entryKey.Substring(modPath.Length + 1);
                            files.Add(relativePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading archive {archivePath}: {ex.Message}");
            }

            return files;
        }

        // Get files from a mod folder
        private List<string> GetFilesFromFolder(string folderPath)
        {
            var files = new List<string>();

            if (!Directory.Exists(folderPath)) return files;

            try
            {
                string baseDir = folderPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                int baseDirLength = baseDir.Length;

                foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(baseDirLength).Replace('\\', '/');
                    files.Add(relativePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading folder {folderPath}: {ex.Message}");
            }

            return files;
        }

        // Update conflict tab with badge showing number of conflicts
        private void UpdateConflictTabBadge(int conflictCount)
        {
            var conflictHeader = ConflictsTab.Header as TextBlock;
            if (conflictHeader != null)
            {
                if (conflictCount > 0)
                {
                    // Show count in the header but keep original styling
                    conflictHeader.Text = $"Conflicts ({conflictCount})";

                    // Keep all original styling - no color change
                    conflictHeader.FontWeight = FontWeights.DemiBold;
                }
                else
                {
                    // Reset to original text
                    conflictHeader.Text = "Conflicts";

                    // Ensure original styling is preserved
                    conflictHeader.FontWeight = FontWeights.DemiBold;
                }

                // Always ensure the style is applied
                if (conflictHeader.Style == null)
                {
                    conflictHeader.Style = FindResource("TabHeaderTextStyle") as Style;
                }

                // Clear any direct foreground property that might override the style
                conflictHeader.ClearValue(TextBlock.ForegroundProperty);
            }
        }

        // Show which mod will win in a conflict
        private void HighlightWinningMods()
        {
            if (ConflictsListView.ItemsSource == null) return;

            var conflictItems = ConflictsListView.ItemsSource as List<ConflictItem>;
            if (conflictItems == null || !conflictItems.Any()) return;

            // Group conflicts by file path
            var conflictsByFile = conflictItems.GroupBy(c => c.FilePath).ToList();

            foreach (var fileGroup in conflictsByFile)
            {
                // Find the mod with the highest priority (lowest in list)
                var winningMod = fileGroup
                    .Select(c => c.Mod)
                    .OrderByDescending(m => _appliedMods.IndexOf(m))
                    .FirstOrDefault();

                if (winningMod != null)
                {
                    // Mark the winning mod in each conflict group
                    foreach (var item in conflictItems.Where(c => c.FilePath == fileGroup.Key))
                    {
                        item.IsWinningMod = item.Mod == winningMod;
                    }
                }
            }

            // Refresh the ListView to show the highlighting
            ConflictsListView.Items.Refresh();
        }

        // Add context menu to Conflicts tab for navigation to mods
        private void AddConflictsContextMenu()
        {
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
        }

        // Jump to the selected mod in the Applied Mods tab
        private void JumpToModMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
            if (selectedItem == null || selectedItem.Mod == null) return;

            // Switch to Applied Mods tab
            ModTabControl.SelectedItem = AppliedModsTab;

            // Find and select the mod in the Applied Mods list
            var modInList = _appliedMods.FirstOrDefault(m => m == selectedItem.Mod);
            if (modInList != null)
            {
                AppliedModsListView.SelectedItem = modInList;
                AppliedModsListView.ScrollIntoView(modInList);
            }
        }

        // Highlight all items with the same conflicting file path
        private void HighlightConflictingFiles_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
            if (selectedItem == null) return;

            string filePath = selectedItem.FilePath;

            // Find all items with the same file path
            foreach (var item in ConflictsListView.Items)
            {
                if (item is ConflictItem conflictItem && conflictItem.FilePath == filePath)
                {
                    // Select these items
                    ConflictsListView.SelectedItems.Add(item);
                }
            }
        }

        // Call from constructor to set up the conflict system
        private void InitializeConflictSystem()
        {
            // Add context menu for conflicts
            AddConflictsContextMenu();

            // Add double-click handler to jump to the mod
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
        }

        private void DeleteModItem(ModInfo mod)
        {
            // Confirm deletion
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{mod.Name}' from your system?\n\nThis action cannot be undone.",
                "Confirm Mod Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // Delete the mod
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // First remove from applied mods if it's there
                if (mod.IsApplied)
                {
                    mod.IsApplied = false;
                    _appliedMods.Remove(mod);

                    // Update priorities
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                }

                // Remove from flat list
                _availableModsFlat.Remove(mod);

                // Delete the actual mod files
                if (mod.IsFromArchive)
                {
                    // Delete the archive file
                    if (!string.IsNullOrEmpty(mod.ArchiveSource) && File.Exists(mod.ArchiveSource))
                    {
                        File.Delete(mod.ArchiveSource);
                    }
                }
                else
                {
                    // Delete the mod folder
                    if (!string.IsNullOrEmpty(mod.ModFolderPath) && Directory.Exists(mod.ModFolderPath))
                    {
                        Directory.Delete(mod.ModFolderPath, true);
                    }
                }

                // Rebuild the category list
                _availableModsCategories = _availableModsFlat.GroupByCategory();
                AvailableModsTreeView.ItemsSource = _availableModsCategories;

                // Mark that changes need to be applied
                _configService.MarkModsChanged();
                SaveAppliedMods();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting mod '{mod.Name}': {ex.Message}", "Delete Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void InitializeProfiles()
        {
            try
            {
                App.LogToFile("Initializing profiles");

                // Clear existing profiles collection
                _profiles.Clear();

                // Get profiles for the current game
                if (_currentGame != null)
                {
                    // Get profiles for this game
                    var gameProfiles = _profileService.GetProfilesForGame(_currentGame.Id);

                    // Add them to the collection
                    foreach (var profile in gameProfiles)
                    {
                        _profiles.Add(profile);
                    }

                    // Get the active profile
                    _activeProfile = _profileService.GetActiveProfile(_currentGame.Id);

                    App.LogToFile($"Loaded {_profiles.Count} profiles for game {_currentGame.Name}");
                }
                else
                {
                    // If no game is selected, just add a default profile
                    _profiles.Add(new ModProfile { Name = "Default Profile" });
                    _activeProfile = _profiles[0];
                    App.LogToFile("No game selected, using default profile");
                }

                // Update the ComboBox
                ProfileComboBox.Items.Clear();
                foreach (var profile in _profiles)
                {
                    ProfileComboBox.Items.Add(profile.Name);
                }

                // Select the active profile
                int indexToSelect = 0;
                if (_activeProfile != null)
                {
                    // Find the index of the active profile
                    for (int i = 0; i < _profiles.Count; i++)
                    {
                        if (_profiles[i].Id == _activeProfile.Id)
                        {
                            indexToSelect = i;
                            break;
                        }
                    }
                }

                // Set the selection
                if (ProfileComboBox.Items.Count > 0)
                {
                    ProfileComboBox.SelectedIndex = indexToSelect;
                }

                // Connect the selection changed event
                ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

                App.LogToFile("Profile initialization complete");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error initializing profiles: {ex.Message}");

                // Fallback to a simple default setup
                try
                {
                    ProfileComboBox.Items.Clear();
                    ProfileComboBox.Items.Add("Default Profile");
                    ProfileComboBox.SelectedIndex = 0;
                }
                catch
                {
                    // Last resort - ignore all errors
                }
            }
        }

        // Update the ProfileComboBox_SelectionChanged handler
        private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                App.LogToFile("Profile selection changed");

                if (_currentGame == null || _profiles.Count == 0 || ProfileComboBox.SelectedIndex < 0)
                {
                    return;
                }

                // Get the selected profile
                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < _profiles.Count)
                {
                    ModProfile selectedProfile = _profiles[selectedIndex];

                    // If it's already the active profile, do nothing
                    if (_activeProfile != null && selectedProfile.Id == _activeProfile.Id)
                    {
                        return;
                    }

                    App.LogToFile($"Switching to profile: {selectedProfile.Name}");

                    try
                    {
                        // Save current mods to the old active profile
                        if (_activeProfile != null)
                        {
                            var currentMods = _appliedMods.Select(m => new AppliedModSetting
                            {
                                ModFolderPath = m.ModFolderPath,
                                IsActive = m.IsActive,
                                IsFromArchive = m.IsFromArchive,
                                ArchiveSource = m.ArchiveSource,
                                ArchiveRootPath = m.ArchiveRootPath
                            }).ToList();

                            await _profileService.UpdateActiveProfileModsAsync(_currentGame.Id, currentMods);
                            App.LogToFile($"Saved current mods to profile '{_activeProfile.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error saving current profile: {ex.Message}");
                        // Continue anyway
                    }

                    // Update the active profile
                    _activeProfile = selectedProfile;

                    // Tell the service this is now the active profile
                    await _profileService.SetActiveProfileAsync(_currentGame.Id, _activeProfile.Id);

                    // Load mods from this profile
                    LoadAppliedModsFromProfile(_activeProfile);

                    App.LogToFile("Profile switch complete");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error in profile selection changed: {ex.Message}");
            }
        }

        private string ShowInputDialog(string title, string message, string defaultValue = "")
        {
            // Create a simple input dialog
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

            // Create layout
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };

            // Add message
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"]
            });

            // Add textbox
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

            // Add buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)Application.Current.Resources["PrimaryButton"],
                Foreground = Brushes.Black,
                IsDefault = true
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Style = (Style)Application.Current.Resources["ActionButton"],
                IsCancel = true
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);
            panel.Children.Add(buttonPanel);

            // Set content and focus textbox
            dialog.Content = panel;
            dialog.Loaded += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            // Show dialog and return result
            bool? result = dialog.ShowDialog();
            return result == true ? textBox.Text : null;
        }

        // Add the wire-up to InitializeUiAsync
        private void InitializeUi()
        {
            try
            {
                App.LogToFile("Initializing profile UI components");

                // Wire up the New Profile button
                if (NewProfileButton != null)
                {
                    NewProfileButton.Click += NewProfileButton_Click;
                    App.LogToFile("New Profile button wired up");
                }

                App.LogToFile("Profile UI initialization complete");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error initializing UI: {ex.Message}");
            }
        }

        private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LogToFile("New Profile button clicked");

                if (_currentGame == null)
                {
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Show input dialog to get profile name
                string profileName = ShowInputDialog("New Profile", "Enter profile name:", "New Profile");

                if (string.IsNullOrEmpty(profileName))
                {
                    App.LogToFile("User cancelled or entered empty name");
                    return;
                }

                App.LogToFile($"Creating new profile: {profileName}");

                // Show wait cursor
                Mouse.OverrideCursor = Cursors.Wait;

                // Create the profile
                var newProfile = await _profileService.CreateProfileAsync(_currentGame.Id, profileName);

                // Restore cursor
                Mouse.OverrideCursor = null;

                if (newProfile != null)
                {
                    App.LogToFile($"New profile created: {newProfile.Name}");

                    // Add to collection
                    _profiles.Add(newProfile);

                    // Refresh the ComboBox
                    InitializeProfiles();

                    // Show success message
                    MessageBox.Show($"Profile '{newProfile.Name}' created successfully.", "Profile Created",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    App.LogToFile("Failed to create profile");
                    MessageBox.Show("Failed to create the profile. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // Restore cursor in case of error
                Mouse.OverrideCursor = null;

                App.LogToFile($"Error creating profile: {ex.Message}");
                MessageBox.Show($"Error creating profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAppliedModsFromProfile(ModProfile profile)
        {
            try
            {
                App.LogToFile($"Loading mods from profile: {profile.Name}");

                // Clear current applied mods
                _appliedMods.Clear();

                if (profile?.AppliedMods == null || profile.AppliedMods.Count == 0)
                {
                    App.LogToFile("No mods in profile");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                App.LogToFile($"Profile contains {profile.AppliedMods.Count} mods");

                // For each saved mod in the profile, find the corresponding mod in available mods
                foreach (var setting in profile.AppliedMods)
                {
                    try
                    {
                        // First try to find mod in available mods
                        var mod = _availableModsFlat.FirstOrDefault(m =>
                            m.ModFolderPath == setting.ModFolderPath ||
                            (m.IsFromArchive && m.ArchiveSource == setting.ArchiveSource));

                        if (mod != null)
                        {
                            // Set properties and add to applied mods
                            mod.IsApplied = true;
                            mod.IsActive = setting.IsActive;
                            _appliedMods.Add(mod);
                            App.LogToFile($"Added mod to applied list: {mod.Name}");
                        }
                        else
                        {
                            App.LogToFile($"Mod not found in available mods: {setting.ModFolderPath ?? setting.ArchiveSource}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error adding mod from profile: {ex.Message}");
                        // Continue with next mod
                    }
                }

                // Show or hide ListView based on whether we have applied mods
                AppliedModsListView.Visibility = _appliedMods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                // Update mod priorities and conflicts
                try
                {
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                    DetectModConflicts();
                    App.LogToFile("Updated mod priorities and conflicts");
                }
                catch (Exception ex)
                {
                    App.LogToFile($"Error updating mod priorities: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading mods from profile: {ex.Message}");
            }
        }

        private void LoadProfilesIntoDropdown()
        {
            try
            {
                App.LogToFile("Loading profiles into dropdown");

                // Clear dropdown before adding items
                ProfileComboBox.Items.Clear();

                // Get profiles for current game
                if (_currentGame != null)
                {
                    // Get profiles
                    _profiles = _profileService.GetProfilesForGame(_currentGame.Id);

                    // Get active profile
                    _activeProfile = _profileService.GetActiveProfile(_currentGame.Id);

                    // Add each profile name to dropdown
                    foreach (var profile in _profiles)
                    {
                        ProfileComboBox.Items.Add(profile.Name);
                    }

                    // Select the active profile
                    if (_activeProfile != null)
                    {
                        int index = _profiles.FindIndex(p => p.Id == _activeProfile.Id);
                        if (index >= 0 && index < ProfileComboBox.Items.Count)
                        {
                            ProfileComboBox.SelectedIndex = index;
                        }
                        else if (ProfileComboBox.Items.Count > 0)
                        {
                            // Default to first item
                            ProfileComboBox.SelectedIndex = 0;
                        }
                    }
                    else if (ProfileComboBox.Items.Count > 0)
                    {
                        ProfileComboBox.SelectedIndex = 0;
                    }

                    App.LogToFile($"Loaded {_profiles.Count} profiles, selected index: {ProfileComboBox.SelectedIndex}");
                }
                else
                {
                    // No game selected, just add default item
                    ProfileComboBox.Items.Add("Default Profile");
                    ProfileComboBox.SelectedIndex = 0;
                    App.LogToFile("No game selected, added default profile placeholder");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading profiles: {ex.Message}");
                // Fallback to a default item
                try
                {
                    ProfileComboBox.Items.Clear();
                    ProfileComboBox.Items.Add("Default Profile");
                    ProfileComboBox.SelectedIndex = 0;
                }
                catch { /* Ignore any further errors */ }
            }
        }


    }
}
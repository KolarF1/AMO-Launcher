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

        private ObservableCollection<ModInfo> _availableModsFlat;
        private ObservableCollection<ModCategory> _availableModsCategories;
        private ObservableCollection<ModInfo> _appliedMods;

        private GameInfo _currentGame;

        private bool _appliedModsChanged = false;

        private bool _isMaximized = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeConflictSystem();

            _availableModsFlat = new ObservableCollection<ModInfo>();
            _availableModsCategories = new ObservableCollection<ModCategory>();
            _appliedMods = new ObservableCollection<ModInfo>();

            AvailableModsTreeView.ItemsSource = _availableModsCategories;
            AppliedModsListView.ItemsSource = _appliedMods;

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
        }

        private string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                await _configService.LoadSettingsAsync();

                await TrySelectGameAsync();

                InitializeUi();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during startup: {ex.Message}", "Startup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
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
            try
            {
                var settings = await _configService.LoadSettingsAsync();
                string preferredGameId = _configService.GetPreferredGameId();

                if (settings.Games.Count > 0 && !string.IsNullOrEmpty(preferredGameId))
                {
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
                        return;
                    }
                }

                var detectedGames = await _gameDetectionService.ScanForGamesAsync();

                if (detectedGames.Count > 0)
                {
                    if (detectedGames.Count == 1)
                    {
                        SetCurrentGame(detectedGames[0]);
                    }
                    else
                    {
                        ShowGameSelectionDialog();
                    }
                }
                else
                {
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

            AvailableModsTreeView.SelectedItemChanged += AvailableModsTreeView_SelectedItemChanged;

            AvailableModsTreeView.MouseDoubleClick += AvailableModsTreeView_MouseDoubleClick;

            AppliedModsListView.MouseDoubleClick += AppliedModsListView_MouseDoubleClick;
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
                    AddModToApplied(selectedMod);

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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new Views.SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
        {
            ShowGameSelectionDialog();
        }

        private void ApplyModsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGame == null)
            {
                MessageBox.Show("Please select a game first.", "No Game Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItem = AvailableModsTreeView.SelectedItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select one or more mods to apply.", "No Mods Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selectedItem is ModInfo selectedMod)
            {
                AddModToApplied(selectedMod);
                ModTabControl.SelectedItem = AppliedModsTab;
                return;
            }

            if (selectedItem is ModCategory category)
            {
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
                    }
                    else if (hasConflicts && existingMessage != null)
                    {
                        parent.Children.Remove(existingMessage);

                        ConflictsListView.Visibility = Visibility.Visible;
                    }
                }
            }
        }

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
                ShowNoGameSelectedUI();
            }
        }

        private async void SetCurrentGame(GameInfo game)
        {
            _currentGame = game;

            CurrentGameTextBlock.Text = game.Name;

            GameContentPanel.Visibility = Visibility.Visible;

            _appliedModsChanged = false;

            if (!game.Name.Contains("Manager"))
            {
                bool backupReady = await _gameBackupService.EnsureOriginalGameDataBackupAsync(game);
                if (!backupReady)
                {
                    MessageBox.Show(
                        "Game backup was not created. Some mod features may be limited or not work correctly.",
                        "Backup Not Created",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            await ScanForModsAsync();

            InitializeProfiles();
            await LoadAppliedModsAsync();
        }

        private async Task ScanForModsAsync()
        {
            if (_currentGame == null) return;

            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                _availableModsFlat.Clear();
                _availableModsCategories.Clear();

                AvailableModsTreeView.Visibility = Visibility.Collapsed;

                var mods = await _modDetectionService.ScanForModsAsync(_currentGame);

                foreach (var mod in mods)
                {
                    _availableModsFlat.Add(mod);
                }

                var categorizedMods = _availableModsFlat.GroupByCategory();

                System.Diagnostics.Debug.WriteLine($"Found {categorizedMods.Count} categories:");
                foreach (var category in categorizedMods)
                {
                    System.Diagnostics.Debug.WriteLine($"  Category: {category.Name}, Mods: {category.Mods.Count}");
                    foreach (var mod in category.Mods)
                    {
                        System.Diagnostics.Debug.WriteLine($"    - {mod.Name}, Category: {mod.Category}");
                    }
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
                MessageBox.Show($"Error scanning for mods: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async Task LoadAppliedModsAsync()
        {
            try
            {
                App.LogToFile("Loading applied mods");

                _appliedMods.Clear();

                if (_currentGame == null)
                {
                    App.LogToFile("No game selected");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                string normalizedGameId = GetNormalizedCurrentGameId();
                App.LogToFile($"Loading mods for normalized game ID: {normalizedGameId}");

                if (_activeProfile != null && _activeProfile.AppliedMods != null)
                {
                    App.LogToFile($"Loading mods from active profile: {_activeProfile.Name} (ID: {_activeProfile.Id})");
                    App.LogToFile($"Profile has {_activeProfile.AppliedMods.Count} mods");

                    if (_activeProfile.AppliedMods.Count > 0)
                    {
                        LoadAppliedModsFromProfile(_activeProfile);
                        return;
                    }
                    else
                    {
                        App.LogToFile("Active profile has no mods, checking config service");
                    }
                }
                else
                {
                    App.LogToFile("No active profile or profile has no mods list");
                }

                var appliedModSettings = _configService.GetAppliedMods(normalizedGameId);

                if ((appliedModSettings == null || appliedModSettings.Count == 0) && normalizedGameId != _currentGame.Id)
                {
                    App.LogToFile($"No mods found with normalized ID, trying original ID: {_currentGame.Id}");
                    appliedModSettings = _configService.GetAppliedMods(_currentGame.Id);
                }

                if (appliedModSettings == null || appliedModSettings.Count == 0)
                {
                    App.LogToFile("No applied mods found in config service");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                App.LogToFile($"Found {appliedModSettings.Count} mods in config service");

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
                            App.LogToFile($"Added mod from config: {mod.Name}");
                        }
                        else if (setting.IsFromArchive && !string.IsNullOrEmpty(setting.ArchiveSource))
                        {
                            App.LogToFile($"Trying to load archive mod: {setting.ArchiveSource}");
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
                    }
                }

                AppliedModsListView.Visibility = _appliedMods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                UpdateModPriorities();
                UpdateModPriorityDisplays();
                DetectModConflicts();

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

        private async void RefreshModsButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanForModsAsync();
        }

        private BitmapImage TryExtractIcon(string executablePath)
        {
            try
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                    return null;

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

        private void ShowNoGameSelectedUI()
        {
            _currentGame = null;

            CurrentGameTextBlock.Text = "No game selected";

            GameContentPanel.Visibility = Visibility.Visible;

            _availableModsFlat.Clear();
            AvailableModsTreeView.Visibility = Visibility.Collapsed;

            _appliedMods.Clear();
            AppliedModsListView.Visibility = Visibility.Collapsed;
        }

        private void AddModToApplied(ModInfo mod)
        {
            if (mod == null) return;

            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                App.LogToFile($"Adding mod to applied list: {mod.Name}");

                string modPath = mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath;
                string absoluteModPath = PathUtility.ToAbsolutePath(modPath);

                bool alreadyExists = _appliedMods.Any(m =>
                    PathUtility.ToAbsolutePath(m.ModFolderPath) == absoluteModPath ||
                    (m.IsFromArchive && PathUtility.ToAbsolutePath(m.ArchiveSource) == absoluteModPath));

                if (!alreadyExists)
                {
                    App.LogToFile($"Mod not in applied list, adding it");

                    mod.IsApplied = true;
                    mod.IsActive = true;

                    _appliedMods.Add(mod);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();

                    AppliedModsListView.Visibility = Visibility.Visible;

                    _configService.MarkModsChanged();

                    App.LogToFile($"Original mod path: {modPath}");
                    App.LogToFile($"Relative path: {PathUtility.ToRelativePath(modPath)}");

                    SaveAppliedMods();
                    DetectModConflicts();
                }
                else
                {
                    App.LogToFile($"Mod already in applied list, skipping");
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error adding mod to applied list: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ActiveCheckBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            _configService.MarkModsChanged();
            SaveAppliedMods();
            DetectModConflicts();
        }

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

        private void RemoveSelectedAppliedMods()
        {
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

                if (_appliedMods.Count == 0)
                {
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                }

                _configService.MarkModsChanged();
                SaveAppliedMods();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

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

        private async Task SaveAppliedModsAsync()
        {
            try
            {
                if (_currentGame == null)
                {
                    App.LogToFile("No game selected, cannot save mods");
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

                App.LogToFile($"Saving {appliedModSettings.Count} mods for game {_currentGame.Name}");
                foreach (var mod in appliedModSettings)
                {
                    App.LogToFile($"  Saving mod path: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}");
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
                    App.LogToFile($"Updated active profile '{_activeProfile.Name}' with {appliedModSettings.Count} mods");
                }

                await _profileService.UpdateActiveProfileModsAsync(normalizedGameId, appliedModSettings);

                App.LogToFile("Mods saved successfully");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving applied mods: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        private void SaveAppliedMods()
        {
            Task.Run(async () => await SaveAppliedModsAsync()).ConfigureAwait(false);
        }

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
                Mouse.OverrideCursor = Cursors.Wait;

                System.Diagnostics.Debug.WriteLine($"Launching game: {_currentGame.Name}");

                await CheckAndApplyModsAsync();

                System.Diagnostics.Debug.WriteLine($"Starting process: {_currentGame.ExecutablePath}");
                Process.Start(_currentGame.ExecutablePath);

                _configService.ResetModsChangedFlag();

                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching game: {ex.Message}", "Launch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async Task CheckAndApplyModsAsync()
        {
            if (_currentGame.Name.Contains("Manager"))
            {
                return;
            }

            try
            {
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
                    System.Diagnostics.Debug.WriteLine("No changes detected in mods - launching game without file operations");
                    return;
                }

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

                    string gameInstallDir = _currentGame.InstallDirectory;
                    string backupDir = Path.Combine(gameInstallDir, "Original_GameData");

                    if (!Directory.Exists(backupDir))
                    {
                        progressWindow.ShowError("Original game data backup not found. Please reset your game data from the Settings window.");
                        await Task.Delay(3000);
                        return;
                    }

                    await Task.Run(() =>
                    {
                        progressWindow.UpdateProgress(0.2, "Restoring original game files...");
                        CopyDirectoryContents(backupDir, gameInstallDir);
                    });

                    if (!hasActiveMods)
                    {
                        progressWindow.UpdateProgress(0.9, "Game restored to original state and ready to launch!");
                    }
                    else
                    {
                        var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

                        for (int i = 0; i < activeMods.Count; i++)
                        {
                            var mod = activeMods[i];
                            double progress = 0.2 + (i * 0.7 / activeMods.Count);

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

                    _configService.SaveLastAppliedModsState(_currentGame.Id, currentActiveMods);
                    System.Diagnostics.Debug.WriteLine($"After save, checking if state exists: {_configService.GetLastAppliedModsState(_currentGame.Id) != null}");
                    _configService.ResetModsChangedFlag();

                    progressWindow.UpdateProgress(1.0, "Ready to launch game!");
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
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
                MessageBox.Show($"Error preparing game launch: {ex.Message}", "Launch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateModPriorities()
        {
            int count = _appliedMods.Count;
            for (int i = 0; i < count; i++)
            {
                _appliedMods[i].Priority = count - i;
            }
            AppliedModsListView.Items.Refresh();
        }

        private void CopyDirectoryContents(string sourceDir, string targetDir, Views.BackupProgressWindow progressWindow = null)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(directory);
                string destDir = Path.Combine(targetDir, dirName);
                CopyDirectoryContents(directory, destDir, progressWindow);
            }
        }

        private void ApplyModFromArchive(ModInfo mod, string targetDir)
        {
            if (string.IsNullOrEmpty(mod.ArchiveSource) || !File.Exists(mod.ArchiveSource))
            {
                return;
            }

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

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    string entryKey = entry.Key.Replace('\\', '/');
                    if (entryKey.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        string relativePath = entryKey.Substring(modPath.Length + 1);
                        string targetPath = Path.Combine(targetDir, relativePath);

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
                    }
                }
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

        public GameInfo GetCurrentGame()
        {
            return _currentGame;
        }

        private void UpdateModPriorityDisplays()
        {
            int count = _appliedMods.Count;
            for (int i = 0; i < count; i++)
            {
                _appliedMods[i].PriorityDisplay = (count - i).ToString();
            }
        }

        private void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu == null) return;

            var treeView = contextMenu.PlacementTarget as TreeView;
            if (treeView == null) return;

            var selectedItem = treeView.SelectedItem;
            if (selectedItem == null) return;

            if (selectedItem is ModInfo selectedMod)
            {
                DeleteModItem(selectedMod);
            }
            else if (selectedItem is ModCategory category)
            {
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

            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                foreach (var mod in _appliedMods)
                {
                    mod.IsActive = false;
                }

                _configService.MarkModsChanged();
                SaveAppliedMods();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void AppliedModsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject originalSource = (DependencyObject)e.OriginalSource;
            while (originalSource != null && !(originalSource is ListViewItem))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (originalSource is ListViewItem)
            {
                if (AppliedModsListView.SelectedItem is ModInfo selectedMod)
                {
                    selectedMod.IsApplied = false;
                    _appliedMods.Remove(selectedMod);
                    UpdateModPriorities();
                    UpdateModPriorityDisplays();

                    if (_appliedMods.Count == 0)
                    {
                        AppliedModsListView.Visibility = Visibility.Collapsed;
                    }

                    _configService.MarkModsChanged();

                    SaveAppliedMods();
                }
            }
        }

        private List<ModFileConflict> _modConflicts = new List<ModFileConflict>();

        private void DetectModConflicts()
        {
            _modConflicts.Clear();

            var activeMods = _appliedMods.Where(m => m.IsActive).ToList();

            if (activeMods.Count < 2)
            {
                UpdateConflictsListView();
                return;
            }

            var fileToModsMap = new Dictionary<string, List<ModInfo>>();

            foreach (var mod in activeMods)
            {
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

            UpdateConflictsListView();
            HighlightWinningMods();
        }

        private void UpdateConflictsListView()
        {
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
        }

        private List<string> GetModFiles(ModInfo mod)
        {
            var files = new List<string>();

            try
            {
                if (mod.IsFromArchive)
                {
                    files = GetFilesFromArchive(mod.ArchiveSource, mod.ArchiveRootPath);
                }
                else if (!string.IsNullOrEmpty(mod.ModFilesPath) && Directory.Exists(mod.ModFilesPath))
                {
                    files = GetFilesFromFolder(mod.ModFilesPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting files for mod {mod.Name}: {ex.Message}");
            }

            return files;
        }

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

        private void UpdateConflictTabBadge(int conflictCount)
        {
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
        }

        private void HighlightWinningMods()
        {
            if (ConflictsListView.ItemsSource == null) return;

            var conflictItems = ConflictsListView.ItemsSource as List<ConflictItem>;
            if (conflictItems == null || !conflictItems.Any()) return;

            var conflictsByFile = conflictItems.GroupBy(c => c.FilePath).ToList();

            foreach (var fileGroup in conflictsByFile)
            {
                var winningMod = fileGroup
                    .Select(c => c.Mod)
                    .OrderByDescending(m => _appliedMods.IndexOf(m))
                    .FirstOrDefault();

                if (winningMod != null)
                {
                    foreach (var item in conflictItems.Where(c => c.FilePath == fileGroup.Key))
                    {
                        item.IsWinningMod = item.Mod == winningMod;
                    }
                }
            }

            ConflictsListView.Items.Refresh();
        }

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

        private void JumpToModMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
            if (selectedItem == null || selectedItem.Mod == null) return;

            ModTabControl.SelectedItem = AppliedModsTab;

            var modInList = _appliedMods.FirstOrDefault(m => m == selectedItem.Mod);
            if (modInList != null)
            {
                AppliedModsListView.SelectedItem = modInList;
                AppliedModsListView.ScrollIntoView(modInList);
            }
        }

        private void HighlightConflictingFiles_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = ConflictsListView.SelectedItem as ConflictItem;
            if (selectedItem == null) return;

            string filePath = selectedItem.FilePath;

            foreach (var item in ConflictsListView.Items)
            {
                if (item is ConflictItem conflictItem && conflictItem.FilePath == filePath)
                {
                    ConflictsListView.SelectedItems.Add(item);
                }
            }
        }

        private void InitializeConflictSystem()
        {
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
        }

        private void DeleteModItem(ModInfo mod)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{mod.Name}' from your system?\n\nThis action cannot be undone.",
                "Confirm Mod Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                if (mod.IsApplied)
                {
                    mod.IsApplied = false;
                    _appliedMods.Remove(mod);

                    UpdateModPriorities();
                    UpdateModPriorityDisplays();
                }

                _availableModsFlat.Remove(mod);

                if (mod.IsFromArchive)
                {
                    if (!string.IsNullOrEmpty(mod.ArchiveSource) && File.Exists(mod.ArchiveSource))
                    {
                        File.Delete(mod.ArchiveSource);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(mod.ModFolderPath) && Directory.Exists(mod.ModFolderPath))
                    {
                        Directory.Delete(mod.ModFolderPath, true);
                    }
                }

                _availableModsCategories = _availableModsFlat.GroupByCategory();
                AvailableModsTreeView.ItemsSource = _availableModsCategories;

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

                _profiles.Clear();

                if (_currentGame != null)
                {
                    string normalizedGameId = GetNormalizedCurrentGameId();
                    App.LogToFile($"Getting profiles for normalized game ID: {normalizedGameId}");

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
                        App.LogToFile($"Loaded {_profiles.Count} profiles for game {_currentGame.Name}");
                    }
                    else
                    {
                        App.LogToFile("No profiles found, creating default profile");
                        var defaultProfile = new ModProfile();
                        _profiles.Add(defaultProfile);

                        _profileService.ImportProfileDirectAsync(normalizedGameId, defaultProfile).ConfigureAwait(false);
                    }

                    _activeProfile = _profileService.GetFullyLoadedActiveProfile(normalizedGameId);

                    App.LogToFile($"Got active profile: {_activeProfile.Name} (ID: {_activeProfile.Id})");
                    App.LogToFile($"Active profile has {_activeProfile.AppliedMods?.Count ?? 0} mods");
                }
                else
                {
                    _profiles.Add(new ModProfile { Name = "Default Profile" });
                    _activeProfile = _profiles[0];
                    App.LogToFile("No game selected, using default profile");
                }

                Dispatcher.Invoke(() => {
                    UpdateProfileComboBox();
                });

                App.LogToFile("Profile initialization complete");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error initializing profiles: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");

                try
                {
                    _profiles.Clear();
                    _profiles.Add(new ModProfile { Name = "Default Profile" });
                    _activeProfile = _profiles[0];

                    Dispatcher.Invoke(() => {
                        UpdateProfileComboBox();
                    });
                }
                catch
                {
                    App.LogToFile("Failed to create fallback profile");
                }
            }
        }

        private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                App.LogToFile("Profile selection changed");

                if (_currentGame == null || _profiles.Count == 0 || ProfileComboBox.SelectedIndex < 0)
                {
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < _profiles.Count)
                {
                    ModProfile selectedProfile = _profiles[selectedIndex];

                    if (_activeProfile != null && selectedProfile.Id == _activeProfile.Id)
                    {
                        App.LogToFile($"Already on profile: {selectedProfile.Name}, no change needed");
                        return;
                    }

                    App.LogToFile($"Switching from '{_activeProfile?.Name}' to '{selectedProfile.Name}'");

                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;

                        if (_activeProfile != null)
                        {
                            App.LogToFile($"Saving current profile '{_activeProfile.Name}' before switching");
                            await SaveCurrentModsToActiveProfile();
                            App.LogToFile("Save completed");
                        }

                        App.LogToFile($"Setting new active profile: {selectedProfile.Name}");

                        _activeProfile = selectedProfile;

                        string normalizedGameId = GetNormalizedCurrentGameId();

                        await _profileService.SetActiveProfileAsync(normalizedGameId, _activeProfile.Id);

                        LoadAppliedModsFromProfile(_activeProfile);

                        App.LogToFile("Profile switch complete");
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error during profile switch: {ex.Message}");
                        App.LogToFile($"Stack trace: {ex.StackTrace}");
                        MessageBox.Show($"Error switching profiles: {ex.Message}", "Profile Switch Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;

                App.LogToFile($"Error in profile selection changed: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        private string ShowInputDialog(string title, string message, string defaultValue = "")
        {
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
            return result == true ? textBox.Text : null;
        }

        private void InitializeUi()
        {
            try
            {
                App.LogToFile("Initializing profile UI components");

                ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
                if (NewProfileButton != null)
                {
                    NewProfileButton.Click += NewProfileButton_Click;
                    App.LogToFile("New Profile button wired up");
                }

                if (DeleteProfileButton != null)
                {
                    DeleteProfileButton.Click += DeleteProfileButton_Click;
                    App.LogToFile("Delete Profile button wired up");
                }

                if (ExportProfileButton != null)
                {
                    ExportProfileButton.Click += ExportProfileButton_Click;
                    App.LogToFile("Export Profile button wired up");
                }

                if (ImportProfileButton != null)
                {
                    ImportProfileButton.Click += ImportProfileButton_Click;
                    App.LogToFile("Import Profile button wired up");
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

                string profileName = ShowInputDialog("New Profile", "Enter profile name:", "New Profile");

                if (string.IsNullOrEmpty(profileName))
                {
                    App.LogToFile("User cancelled or entered empty name");
                    return;
                }

                App.LogToFile($"Creating new profile: {profileName}");

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    if (_activeProfile != null)
                    {
                        App.LogToFile($"Saving current active profile '{_activeProfile?.Name}' before creating new one");
                        await SaveCurrentModsToActiveProfile();
                    }

                    string normalizedGameId = GetNormalizedCurrentGameId();
                    App.LogToFile($"Using normalized game ID: {normalizedGameId}");

                    var newProfile = await _profileService.CreateProfileAsync(normalizedGameId, profileName);

                    if (newProfile != null)
                    {
                        App.LogToFile($"New profile created: {newProfile.Name} with ID {newProfile.Id}");

                        await _profileService.SaveProfilesAsync();

                        string oldActiveProfileId = _activeProfile?.Id;

                        _activeProfile = newProfile;
                        await _profileService.SetActiveProfileAsync(normalizedGameId, _activeProfile.Id);

                        InitializeProfiles();

                        int newProfileIndex = _profiles.FindIndex(p => p.Id == newProfile.Id);

                        ProfileComboBox.SelectionChanged -= ProfileComboBox_SelectionChanged;

                        if (newProfileIndex >= 0 && newProfileIndex < ProfileComboBox.Items.Count)
                        {
                            ProfileComboBox.SelectedIndex = newProfileIndex;
                        }

                        ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;

                        _appliedMods.Clear();
                        AppliedModsListView.Visibility = Visibility.Collapsed;

                        AppliedModsListView.Items.Refresh();

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
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;

                App.LogToFile($"Error creating profile: {ex.Message}");
                MessageBox.Show($"Error creating profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LogToFile("Delete Profile button clicked");

                if (_currentGame == null)
                {
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_profiles.Count <= 1)
                {
                    MessageBox.Show("Cannot delete the only profile. At least one profile must exist.",
                        "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= _profiles.Count)
                {
                    App.LogToFile("No profile selected");
                    return;
                }

                ModProfile selectedProfile = _profiles[selectedIndex];
                App.LogToFile($"Selected profile for deletion: {selectedProfile.Name} (ID: {selectedProfile.Id})");

                var result = MessageBox.Show($"Are you sure you want to delete the profile '{selectedProfile.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    App.LogToFile("User cancelled deletion");
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    App.LogToFile($"Deleting profile {selectedProfile.Name} with ID {selectedProfile.Id}");

                    App.LogToFile($"Current profiles in memory:");
                    foreach (var profile in _profiles)
                    {
                        App.LogToFile($"  - {profile.Name} (ID: {profile.Id})");
                    }

                    bool deleted = await _profileService.DeleteProfileAsync(_currentGame.Id, selectedProfile.Id);
                    App.LogToFile($"DeleteProfileAsync returned: {deleted}");

                    if (deleted)
                    {
                        App.LogToFile($"Profile deleted: {selectedProfile.Name}");

                        await _profileService.SaveProfilesAsync();
                        App.LogToFile("Profiles saved after deletion");

                        InitializeProfiles();

                        if (_activeProfile != null)
                        {
                            LoadAppliedModsFromProfile(_activeProfile);
                        }

                        MessageBox.Show($"Profile '{selectedProfile.Name}' deleted successfully.", "Profile Deleted",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        App.LogToFile("Failed to delete profile - service returned false");
                        MessageBox.Show("Failed to delete the profile. Please try again.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    App.LogToFile($"Exception during profile deletion: {ex.Message}");
                    App.LogToFile($"Stack trace: {ex.StackTrace}");
                    MessageBox.Show($"Error deleting profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
            catch (Exception ex)
            {

                Mouse.OverrideCursor = null;

                App.LogToFile($"Error in DeleteProfileButton_Click: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error deleting profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LogToFile("Export Profile button clicked");

                if (_currentGame == null)
                {
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int selectedIndex = ProfileComboBox.SelectedIndex;
                if (selectedIndex < 0 || selectedIndex >= _profiles.Count)
                {
                    App.LogToFile("No profile selected for export");
                    MessageBox.Show("Please select a profile to export.", "No Profile Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ModProfile selectedProfile = _profiles[selectedIndex];
                App.LogToFile($"Selected profile for export: {selectedProfile.Name} (ID: {selectedProfile.Id})");

                try
                {
                    var currentMods = _appliedMods.Select(m => new AppliedModSetting
                    {
                        ModFolderPath = m.IsFromArchive ? null : PathUtility.ToRelativePath(m.ModFolderPath),
                        IsActive = m.IsActive,
                        IsFromArchive = m.IsFromArchive,
                        ArchiveSource = m.IsFromArchive ? PathUtility.ToRelativePath(m.ArchiveSource) : null,
                        ArchiveRootPath = m.ArchiveRootPath
                    }).ToList();

                    await _profileService.UpdateActiveProfileModsAsync(_currentGame.Id, currentMods);
                    App.LogToFile("Saved current mods to profile before export");
                }
                catch (Exception ex)
                {
                    App.LogToFile($"Error saving current profile before export: {ex.Message}");
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Profile",
                    Filter = "JSON Files (*.json)|*.json",
                    DefaultExt = ".json",
                    FileName = $"{_currentGame.Name}_{selectedProfile.Name}_{DateTime.Now:yyyyMMdd}.json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    App.LogToFile($"Selected export file: {saveFileDialog.FileName}");

                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        var exportProfile = CreateExportCopy(selectedProfile);

                        App.LogToFile($"Exporting profile {selectedProfile.Name} to {saveFileDialog.FileName}");

                        string json = System.Text.Json.JsonSerializer.Serialize(exportProfile, new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                        File.WriteAllText(saveFileDialog.FileName, json);
                        App.LogToFile($"Profile JSON written to file successfully");

                        Mouse.OverrideCursor = null;

                        App.LogToFile($"Profile exported to: {saveFileDialog.FileName}");
                        MessageBox.Show($"Profile '{selectedProfile.Name}' exported successfully.", "Profile Exported",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;

                        App.LogToFile($"Error exporting profile: {ex.Message}");
                        App.LogToFile($"Stack trace: {ex.StackTrace}");
                        MessageBox.Show($"Error exporting profile: {ex.Message}", "Export Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    App.LogToFile("Export cancelled by user");
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;

                App.LogToFile($"Error in ExportProfileButton_Click: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error exporting profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LogToFile("Import Profile button clicked");

                if (_currentGame == null)
                {
                    MessageBox.Show("Please select a game first.", "No Game Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Profile",
                    Filter = "JSON Files (*.json)|*.json",
                    DefaultExt = ".json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    App.LogToFile($"Selected import file: {openFileDialog.FileName}");

                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        App.LogToFile($"Reading file content from {openFileDialog.FileName}");

                        string json = File.ReadAllText(openFileDialog.FileName);

                        var importedProfile = System.Text.Json.JsonSerializer.Deserialize<ModProfile>(json);

                        if (importedProfile == null)
                        {
                            throw new Exception("Failed to deserialize profile data");
                        }

                        string oldId = importedProfile.Id;
                        importedProfile.Id = Guid.NewGuid().ToString();
                        importedProfile.LastModified = DateTime.Now;

                        App.LogToFile($"Imported profile: {importedProfile.Name} with {importedProfile.AppliedMods?.Count ?? 0} mods");
                        App.LogToFile($"Changed ID from {oldId} to {importedProfile.Id}");

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

                        var imported = await _profileService.ImportProfileDirectAsync(_currentGame.Id, importedProfile);

                        if (imported == null)
                        {
                            throw new Exception("Failed to import profile to service");
                        }

                        await _profileService.SetActiveProfileAsync(_currentGame.Id, imported.Id);

                        App.LogToFile($"Added and set as active profile: {imported.Name}");

                        await _profileService.SaveProfilesAsync();
                        App.LogToFile("Profiles saved to storage after import");

                        InitializeProfiles();
                        LoadProfilesIntoDropdown();

                        _activeProfile = imported;
                        LoadAppliedModsFromProfile(_activeProfile);

                        Mouse.OverrideCursor = null;

                        App.LogToFile($"Profile imported successfully");
                        MessageBox.Show($"Profile '{imported.Name}' imported successfully.", "Profile Imported",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;

                        App.LogToFile($"Error importing profile: {ex.Message}");
                        App.LogToFile($"Stack trace: {ex.StackTrace}");
                        MessageBox.Show($"Error importing profile: {ex.Message}", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    App.LogToFile("Import cancelled by user");
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;

                App.LogToFile($"Error in ImportProfileButton_Click: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error importing profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAppliedModsFromProfile(ModProfile profile)
        {
            try
            {
                App.LogToFile($"Loading mods from profile: {profile.Name} (ID: {profile.Id})");

                _appliedMods.Clear();

                if (profile == null)
                {
                    App.LogToFile("Profile is null");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                if (profile.AppliedMods == null)
                {
                    profile.AppliedMods = new List<AppliedModSetting>();
                    App.LogToFile("Profile had null AppliedMods list, initialized as empty");
                }

                if (profile.AppliedMods.Count == 0)
                {
                    App.LogToFile("No mods in profile");
                    AppliedModsListView.Visibility = Visibility.Collapsed;
                    return;
                }

                App.LogToFile($"Profile contains {profile.AppliedMods.Count} mods");

                foreach (var mod in profile.AppliedMods)
                {
                    App.LogToFile($"  Profile mod: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}, Active: {mod.IsActive}");
                }

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

                foreach (var setting in profile.AppliedMods)
                {
                    try
                    {
                        if (setting == null)
                        {
                            App.LogToFile("Skipping null mod setting");
                            continue;
                        }

                        string searchPath = setting.IsFromArchive
                            ? PathUtility.ToAbsolutePath(setting.ArchiveSource)
                            : PathUtility.ToAbsolutePath(setting.ModFolderPath);

                        if (string.IsNullOrEmpty(searchPath))
                        {
                            App.LogToFile("Skipping mod with empty path");
                            continue;
                        }

                        App.LogToFile($"Looking for mod: {searchPath}");

                        if (availableMods.TryGetValue(searchPath, out var mod))
                        {
                            mod.IsApplied = true;
                            mod.IsActive = setting.IsActive;
                            _appliedMods.Add(mod);
                            App.LogToFile($"Added mod to applied list: {mod.Name}");
                            continue;
                        }

                        if (setting.IsFromArchive && !string.IsNullOrEmpty(setting.ArchiveSource))
                        {
                            App.LogToFile($"Mod not found in available mods, trying to load archive directly");

                            try
                            {
                                string absoluteArchivePath = PathUtility.ToAbsolutePath(setting.ArchiveSource);
                                App.LogToFile($"Absolute archive path: {absoluteArchivePath}");

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
                                            App.LogToFile($"Added archive mod: {archiveMod.Name}");
                                        }
                                        else
                                        {
                                            App.LogToFile("Archive mod loading returned null");
                                        }
                                    }
                                    else
                                    {
                                        App.LogToFile("Archive loading timed out");
                                    }
                                }
                                else
                                {
                                    App.LogToFile($"Archive file not found: {absoluteArchivePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogToFile($"Error loading archive mod: {ex.Message}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(setting.ModFolderPath))
                        {
                            App.LogToFile($"Mod not found in available mods, trying to load from folder directly");

                            try
                            {
                                string absoluteFolderPath = PathUtility.ToAbsolutePath(setting.ModFolderPath);
                                App.LogToFile($"Absolute folder path: {absoluteFolderPath}");

                                if (Directory.Exists(absoluteFolderPath))
                                {
                                    var folderMod = _modDetectionService.LoadModFromFolderPath(absoluteFolderPath, _currentGame.Name);
                                    if (folderMod != null)
                                    {
                                        folderMod.IsApplied = true;
                                        folderMod.IsActive = setting.IsActive;
                                        _appliedMods.Add(folderMod);
                                        App.LogToFile($"Added folder mod: {folderMod.Name}");
                                    }
                                    else
                                    {
                                        App.LogToFile("Folder mod loading returned null");
                                    }
                                }
                                else
                                {
                                    App.LogToFile($"Mod folder not found: {absoluteFolderPath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogToFile($"Error loading folder mod: {ex.Message}");
                            }
                        }
                        else
                        {
                            App.LogToFile("Could not find or load mod - missing path information");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogToFile($"Error adding mod from profile: {ex.Message}");
                    }
                }

                Dispatcher.Invoke(() => {
                    AppliedModsListView.Visibility = _appliedMods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                    AppliedModsListView.Items.Refresh();
                });

                UpdateModPriorities();
                UpdateModPriorityDisplays();
                DetectModConflicts();
                App.LogToFile($"Updated mod priorities and conflicts, applied mods count: {_appliedMods.Count}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error loading mods from profile: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        private void LoadProfilesIntoDropdown()
        {
            UpdateProfileComboBox();
        }

        private ModProfile CreateExportCopy(ModProfile original)
        {
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

            App.LogToFile($"Created export copy of profile with {copy.AppliedMods.Count} mods, all with relative paths");

            return copy;
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
            try
            {
                ProfileComboBox.Items.Clear();

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
                        App.LogToFile($"Selected profile at index {indexToSelect}: {_activeProfile.Name}");
                    }
                    else if (ProfileComboBox.Items.Count > 0)
                    {
                        ProfileComboBox.SelectedIndex = 0;
                        App.LogToFile($"Active profile not found in list, selected first profile");
                    }
                }
                else if (ProfileComboBox.Items.Count > 0)
                {
                    ProfileComboBox.SelectedIndex = 0;
                    App.LogToFile($"No active profile, selected first profile");
                }

                App.LogToFile($"Loaded {_profiles.Count} profiles, selected index: {ProfileComboBox.SelectedIndex}");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error updating profile ComboBox: {ex.Message}");
            }
        }

        private async Task SaveCurrentModsToActiveProfile()
        {
            try
            {
                if (_activeProfile == null || _currentGame == null)
                {
                    App.LogToFile("Cannot save mods - no active profile or game");
                    return;
                }

                App.LogToFile($"SaveCurrentModsToActiveProfile: Saving mods to '{_activeProfile.Name}' (ID: {_activeProfile.Id})");

                var currentMods = _appliedMods.Select(m => new AppliedModSetting
                {
                    ModFolderPath = m.IsFromArchive ? null : PathUtility.ToRelativePath(m.ModFolderPath),
                    IsActive = m.IsActive,
                    IsFromArchive = m.IsFromArchive,
                    ArchiveSource = m.IsFromArchive ? PathUtility.ToRelativePath(m.ArchiveSource) : null,
                    ArchiveRootPath = m.ArchiveRootPath
                }).ToList();

                App.LogToFile($"Saving {currentMods.Count} mods to active profile");
                foreach (var mod in currentMods)
                {
                    App.LogToFile($"  - Mod: {(mod.IsFromArchive ? mod.ArchiveSource : mod.ModFolderPath)}, Active: {mod.IsActive}");
                }

                _activeProfile.AppliedMods = new List<AppliedModSetting>(currentMods);
                _activeProfile.LastModified = DateTime.Now;

                string normalizedGameId = GetNormalizedCurrentGameId();
                await _profileService.UpdateActiveProfileModsAsync(normalizedGameId, currentMods);

                await _configService.SaveAppliedModsAsync(normalizedGameId, currentMods);
                if (normalizedGameId != _currentGame.Id)
                {
                    await _configService.SaveAppliedModsAsync(_currentGame.Id, currentMods);
                }

                App.LogToFile($"Successfully saved {currentMods.Count} mods to profile '{_activeProfile.Name}'");
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error saving current mods to profile: {ex.Message}");
                App.LogToFile($"Stack trace: {ex.StackTrace}");
            }
        }

        private string NormalizeGameId(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return gameId;

            gameId = gameId.Trim();

            int underscoreIndex = gameId.IndexOf('_');
            if (underscoreIndex > 0)
            {
                return gameId.Substring(0, underscoreIndex);
            }

            return gameId;
        }

        private string GetNormalizedCurrentGameId()
        {
            if (_currentGame == null)
                return null;

            return NormalizeGameId(_currentGame.Id);
        }


    }
}
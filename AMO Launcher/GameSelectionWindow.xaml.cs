using AMO_Launcher.Models;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace AMO_Launcher.Views
{
    public partial class GameSelectionWindow : Window
    {
        private readonly GameDetectionService _gameDetectionService;
        private readonly ConfigurationService _configService;
        private readonly ManualGameIconService _manualGameIconService;
        private ObservableCollection<GameInfo> _games;

        public GameInfo? SelectedGame { get; private set; }

        public GameSelectionWindow(GameDetectionService gameDetectionService, ConfigurationService configService)
        {
            _gameDetectionService = gameDetectionService;
            _configService = configService;
            _manualGameIconService = App.ManualGameIconService;
            _games = new ObservableCollection<GameInfo>();

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Initializing GameSelectionWindow");

                InitializeComponent();
                RemoveColumnHeaderHoverEffects();
                SetupContextMenuBehavior();

                GameListView.ItemsSource = _games;

                GameListView.SizeChanged += (s, e) => ErrorHandler.ExecuteSafe(() => ResizeGridViewColumn(),
                    "Resize ListView columns");

                Loaded += (s, e) => ErrorHandler.ExecuteSafe(() => ResizeGridViewColumn(),
                    "Initial column resize");

                SelectButton.IsEnabled = false;
                MakeDefaultButton.IsEnabled = false;
                RemoveGameButton.IsEnabled = false;

                GameListView.SelectionChanged += (s, e) => ErrorHandler.ExecuteSafe(() =>
                {
                    bool hasSelection = GameListView.SelectedItem != null;
                    SelectButton.IsEnabled = hasSelection;
                    MakeDefaultButton.IsEnabled = hasSelection;
                    RemoveGameButton.IsEnabled = hasSelection;

                    if (hasSelection && GameListView.SelectedItem is GameInfo selectedGame)
                    {
                        App.LogService?.LogDebug($"UI: Game selected: {selectedGame.Name}");
                    }
                }, "Handle selection change");

                GameListView.MouseDoubleClick += (s, e) => ErrorHandler.ExecuteSafe(() =>
                {
                    if (GameListView.SelectedItem != null)
                    {
                        var originalSource = e.OriginalSource as DependencyObject;

                        if (originalSource == null)
                            return;

                        bool clickedOnItem = false;
                        while (originalSource != null)
                        {
                            if (originalSource is ListViewItem)
                            {
                                clickedOnItem = true;
                                break;
                            }
                            originalSource = VisualTreeHelper.GetParent(originalSource);
                        }

                        if (clickedOnItem)
                        {
                            App.LogService?.LogDebug("UI: Double-click selection");
                            SelectButton_Click(this, new RoutedEventArgs());
                            e.Handled = true;
                        }
                    }
                }, "Handle double-click selection");

                Loaded += async (s, e) => await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    await LoadGamesAsync();
                }, "Load games on window initialization");

                App.LogService?.LogDebug("GameSelectionWindow initialization completed");
            }, "Initialize GameSelectionWindow");
        }

        #region Custom Title Bar Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace("UI: Title bar drag initiated");
                this.DragMove();
            }, "Window drag operation");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug("UI: Close button clicked");
                DialogResult = false;
                Close();
            }, "Close window operation");
        }

        #endregion

        private async Task LoadGamesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var operationStopwatch = Stopwatch.StartNew();
                App.LogService.Info("Starting game list loading");

                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    _games.Clear();
                    App.LogService.LogDebug("Cleared existing game list");

                    var settings = await _configService.LoadSettingsAsync();
                    App.LogService.LogDebug($"Loaded settings with {settings.Games.Count} saved games");

                    App.LogService.Info("Scanning for installed games");
                    var scanStopwatch = Stopwatch.StartNew();
                    var detectedGames = await _gameDetectionService.ScanForGamesAsync();
                    scanStopwatch.Stop();
                    App.LogService.LogDebug($"Game detection completed in {scanStopwatch.ElapsedMilliseconds}ms, found {detectedGames.Count} games");

                    var mergedGames = new List<GameInfo>();

                    mergedGames.AddRange(detectedGames);

                    int restoredGameCount = 0;
                    foreach (var savedGame in settings.Games)
                    {
                        if (mergedGames.Any(g => g.ExecutablePath == savedGame.ExecutablePath))
                        {
                            App.LogService.Trace($"Skipping already detected game: {savedGame.Name}");
                            continue;
                        }

                        if (_configService.IsGameRemoved(savedGame.ExecutablePath))
                        {
                            App.LogService.LogDebug($"Skipping removed game: {savedGame.Name}");
                            continue;
                        }

                        if (_gameDetectionService.GameExistsOnDisk(savedGame.ExecutablePath))
                        {
                            App.LogService.LogDebug($"Restoring saved game: {savedGame.Name}");

                            var gameInfo = new GameInfo
                            {
                                Id = savedGame.Id,
                                Name = savedGame.Name,
                                ExecutablePath = savedGame.ExecutablePath,
                                InstallDirectory = savedGame.InstallDirectory ?? Path.GetDirectoryName(savedGame.ExecutablePath) ?? string.Empty,
                                IsDefault = savedGame.IsDefault,
                                IsManuallyAdded = savedGame.IsManuallyAdded
                            };

                            if (savedGame.IsManuallyAdded)
                            {
                                gameInfo.Icon = _manualGameIconService.GetIconForGame(savedGame.ExecutablePath);
                                App.LogService.LogDebug($"Loaded icon for manually added game: {savedGame.Name}");
                            }
                            else
                            {
                                gameInfo.Icon = TryExtractIcon(savedGame.ExecutablePath);
                            }

                            mergedGames.Add(gameInfo);
                            restoredGameCount++;
                        }
                        else
                        {
                            App.LogService.LogDebug($"Saved game no longer exists on disk: {savedGame.Name} at {savedGame.ExecutablePath}");
                        }
                    }

                    App.LogService.LogDebug($"Restored {restoredGameCount} saved games that weren't detected automatically");

                    foreach (var game in mergedGames)
                    {
                        var savedGame = settings.Games.FirstOrDefault(g => g.ExecutablePath == game.ExecutablePath);
                        if (savedGame != null)
                        {
                            game.IsDefault = savedGame.IsDefault;
                            game.IsManuallyAdded = savedGame.IsManuallyAdded;

                            if (string.IsNullOrEmpty(game.InstallDirectory))
                            {
                                game.InstallDirectory = savedGame.InstallDirectory ??
                                                   Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty;
                            }
                        }

                        if (game.IsManuallyAdded && game.Icon == null)
                        {
                            game.Icon = _manualGameIconService.GetIconForGame(game.ExecutablePath);
                            App.LogService.LogDebug($"Loaded missing icon for manually added game: {game.Name}");
                        }
                        else if (!game.IsManuallyAdded && game.Icon == null)
                        {
                            game.Icon = TryExtractIcon(game.ExecutablePath);
                        }

                        if (string.IsNullOrEmpty(game.Id))
                        {
                            game.GenerateId();
                            App.LogService.LogDebug($"Generated new ID for game: {game.Name}");
                        }
                    }

                    var sortedGames = mergedGames.OrderBy(g => g.Name).ToList();
                    App.LogService.LogDebug($"Sorted {sortedGames.Count} games alphabetically");

                    _games.Clear();
                    foreach (var game in sortedGames)
                    {
                        _games.Add(game);
                    }

                    _configService.UpdateGames(_games.ToList());
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Updated and saved game list to settings");

                    string preferredGameId = _configService.GetPreferredGameId();
                    if (!string.IsNullOrEmpty(preferredGameId))
                    {
                        var gameToSelect = _games.FirstOrDefault(g => g.Id == preferredGameId);
                        if (gameToSelect != null)
                        {
                            GameListView.SelectedItem = gameToSelect;
                            App.LogService.LogDebug($"Selected preferred game: {gameToSelect.Name}");
                        }
                        else
                        {
                            App.LogService.LogDebug($"Preferred game with ID {preferredGameId} not found");
                        }
                    }
                    else if (_games.Count > 0)
                    {
                        GameListView.SelectedItem = _games[0];
                        App.LogService.LogDebug($"No preferred game, selected first game: {_games[0].Name}");
                    }

                    GameListView.Items.Refresh();

                    operationStopwatch.Stop();
                    App.LogService.Info($"Game list loading completed in {operationStopwatch.ElapsedMilliseconds}ms - Loaded {_games.Count} games");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }, "Load games async operation", showErrorToUser: true);
        }

        private BitmapImage? TryExtractIcon(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService.LogDebug($"Cannot extract icon - invalid path or file doesn't exist: {executablePath}");
                    return null;
                }

                BitmapImage defaultIcon = new BitmapImage();
                defaultIcon.BeginInit();
                defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                defaultIcon.EndInit();

                try
                {
                    App.LogService.Trace($"Extracting icon from: {Path.GetFileName(executablePath)}");

                    byte[] iconData;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath))
                    {
                        if (icon == null)
                        {
                            App.LogService.LogDebug("ExtractAssociatedIcon returned null, using default icon");
                            return defaultIcon;
                        }

                        using (var bitmap = icon.ToBitmap())
                        using (var stream = new MemoryStream())
                        {
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            iconData = stream.ToArray();
                            App.LogService.Trace($"Extracted icon size: {iconData.Length} bytes");
                        }
                    }

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = new MemoryStream(iconData);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    App.LogService.Trace("Icon extraction successful");
                    return bitmapImage;
                }
                catch (Exception ex)
                {
                    App.LogService.LogDebug($"Icon extraction error: {ex.Message}");
                    return defaultIcon;
                }
            }, $"Extract icon from {Path.GetFileName(executablePath)}", showErrorToUser: false, defaultValue: null);
        }

        private async void ScanGamesButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.Info("Manual game scan initiated by user");

                ScanGamesButton.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await LoadGamesAsync();
                    stopwatch.Stop();

                    App.LogService.Info($"Manual game scan completed in {stopwatch.ElapsedMilliseconds}ms");
                    MessageBox.Show("Game scan completed.", "Scan Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    ScanGamesButton.IsEnabled = true;
                    Mouse.OverrideCursor = null;
                }
            }, "Manual game scan operation", showErrorToUser: true);
        }

        private async void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.Info("Add game manually initiated by user");

                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select F1 Game Executable",
                    Filter = "Game Executable (*.exe)|*.exe",
                    CheckFileExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    App.LogService.LogDebug($"User selected file: {openFileDialog.FileName}");
                    Mouse.OverrideCursor = Cursors.Wait;

                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var gameInfo = _gameDetectionService.AddGameManually(openFileDialog.FileName);

                        if (gameInfo != null)
                        {
                            App.LogService.LogDebug($"Game identified: {gameInfo.Name}");

                            gameInfo.IsManuallyAdded = true;

                            if (gameInfo.Icon == null)
                            {
                                var icon = _manualGameIconService.GetIconForGame(gameInfo.ExecutablePath);
                                if (icon != null)
                                {
                                    gameInfo.Icon = icon;
                                    App.LogService.LogDebug($"Got icon for new manually added game: {gameInfo.Name}");
                                }
                                else
                                {
                                    App.LogService.LogDebug($"No icon found for manually added game: {gameInfo.Name}");
                                }
                            }
                            else
                            {
                                _manualGameIconService.SaveIconForGame(gameInfo.ExecutablePath, gameInfo.Icon);
                                App.LogService.LogDebug($"Saved icon for new manually added game: {gameInfo.Name}");
                            }

                            if (string.IsNullOrEmpty(gameInfo.InstallDirectory))
                            {
                                gameInfo.InstallDirectory = Path.GetDirectoryName(gameInfo.ExecutablePath) ?? string.Empty;
                                App.LogService.LogDebug($"Set install directory to: {gameInfo.InstallDirectory}");
                            }

                            var existingGame = _games.FirstOrDefault(g => g.ExecutablePath == gameInfo.ExecutablePath);
                            if (existingGame != null)
                            {
                                App.LogService.Info($"Game already exists in list: {existingGame.Name}");
                                MessageBox.Show("This game is already in your list.", "Game Already Added",
                                    MessageBoxButton.OK, MessageBoxImage.Information);

                                GameListView.SelectedItem = existingGame;
                            }
                            else
                            {
                                _games.Add(gameInfo);
                                GameListView.SelectedItem = gameInfo;

                                _configService.UpdateGames(_games.ToList());
                                await _configService.SaveSettingsAsync();

                                GameListView.Items.Refresh();

                                stopwatch.Stop();
                                App.LogService.Info($"Game added successfully: {gameInfo.Name} (took {stopwatch.ElapsedMilliseconds}ms)");
                                MessageBox.Show($"{gameInfo.Name} has been added to your game list.", "Game Added",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        else
                        {
                            App.LogService.Warning($"Selected file is not a valid game: {openFileDialog.FileName}");
                            MessageBox.Show("The selected file doesn't appear to be a valid game or cannot be added.",
                                "Invalid Game", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
                else
                {
                    App.LogService.LogDebug("User cancelled file selection");
                }
            }, "Add game manually operation", showErrorToUser: true);
        }

        private async void RemoveGameButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.LogDebug($"Attempting to remove game: {selectedGame.Name}");

                    var result = MessageBox.Show(
                        $"Are you sure you want to remove '{selectedGame.Name}' from the list?\n\nThis will not uninstall the game or delete any files.",
                        "Confirm Removal",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.LogService.Info($"Removing game: {selectedGame.Name}");

                        _configService.AddToRemovedGames(selectedGame.ExecutablePath);
                        App.LogService.LogDebug($"Added to removed games list: {selectedGame.ExecutablePath}");

                        _games.Remove(selectedGame);

                        _configService.UpdateGames(_games.ToList());

                        if (selectedGame.IsDefault)
                        {
                            App.LogService.LogDebug("Removed game was the default, clearing default setting");
                            _configService.SetDefaultGame(string.Empty);
                        }

                        await _configService.SaveSettingsAsync();
                        App.LogService.LogDebug("Saved settings after game removal");

                        GameListView.Items.Refresh();

                        App.LogService.Info($"Game removed successfully: {selectedGame.Name}");
                        MessageBox.Show($"{selectedGame.Name} has been removed from your game list.",
                            "Game Removed", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        App.LogService.LogDebug("User cancelled game removal");
                    }
                }
            }, "Remove game operation", showErrorToUser: true);
        }

        private async void MakeDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.Info($"Setting game as default: {selectedGame.Name}");

                    foreach (var game in _games)
                    {
                        game.IsDefault = false;
                    }

                    selectedGame.IsDefault = true;
                    App.LogService.LogDebug($"Updated IsDefault flag for: {selectedGame.Name}");

                    _configService.SetDefaultGame(selectedGame.Id);
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Saved default game setting");

                    GameListView.Items.Refresh();

                    App.LogService.Info($"Default game set successfully: {selectedGame.Name}");
                    MessageBox.Show($"{selectedGame.Name} has been set as the default game.",
                        "Default Game Set", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, "Set default game operation", showErrorToUser: true);
        }

        private async void DefaultCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var checkBox = sender as CheckBox;
                if (checkBox != null && checkBox.Tag != null)
                {
                    string gameId = checkBox.Tag.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(gameId))
                    {
                        App.LogService.Warning("DefaultCheckBox_Click: Empty or null game ID");
                        return;
                    }

                    var selectedGame = _games.FirstOrDefault(g => g.Id == gameId);
                    if (selectedGame == null)
                    {
                        App.LogService.Warning($"DefaultCheckBox_Click: Game with ID {gameId} not found");
                        return;
                    }

                    App.LogService.Info($"Setting game as default via checkbox: {selectedGame.Name}");

                    foreach (var game in _games)
                    {
                        game.IsDefault = game.Id == gameId;
                    }

                    _configService.SetDefaultGame(gameId);
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Saved default game setting via checkbox");

                    GameListView.Items.Refresh();

                    App.LogService.Info($"Default game set successfully via checkbox: {selectedGame.Name}");
                }
            }, "Default checkbox click operation");
        }

        private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null && !string.IsNullOrEmpty(selectedGame.ExecutablePath))
                {
                    App.LogService.LogDebug($"Copying game path to clipboard: {selectedGame.Name}");
                    Clipboard.SetText(selectedGame.ExecutablePath);
                    App.LogService.LogDebug("Path copied to clipboard successfully");
                }
                else
                {
                    App.LogService.Warning("Cannot copy path - no game selected or path is empty");
                }
            }, "Copy path to clipboard operation");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug("Cancel button clicked");
                DialogResult = false;
                Close();
            }, "Cancel operation");
        }

        private async void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.Info($"Game selected: {selectedGame.Name}");
                    SelectedGame = selectedGame;

                    if (_configService.GetRememberLastSelectedGame())
                    {
                        App.LogService.LogDebug("Saving as last selected game (remember setting enabled)");
                        _configService.SetLastSelectedGame(selectedGame.Id);
                        await _configService.SaveSettingsAsync();
                    }
                    else
                    {
                        App.LogService.LogDebug("Not saving as last selected game (remember setting disabled)");
                    }

                    DialogResult = true;
                    Close();
                }
                else
                {
                    App.LogService.Warning("Select button clicked but no game is selected");
                }
            }, "Select game operation");
        }

        private void ResizeGridViewColumn()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (GameListView.View is GridView gridView)
                {
                    App.LogService.Trace("Resizing grid view columns");

                    double actualWidth = GameListView.ActualWidth;

                    double gameColWidth = 200;
                    double defaultColWidth = 60;

                    bool hasVerticalScrollBar = false;
                    var scrollViewer = FindVisualChild<ScrollViewer>(GameListView);
                    if (scrollViewer != null)
                    {
                        hasVerticalScrollBar = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
                    }

                    double scrollBarWidth = hasVerticalScrollBar ? SystemParameters.VerticalScrollBarWidth : 0;

                    gridView.Columns[0].Width = gameColWidth;
                    gridView.Columns[2].Width = defaultColWidth;

                    double pathColWidth = actualWidth - gameColWidth - defaultColWidth - scrollBarWidth - 2;

                    if (pathColWidth > 50)
                    {
                        gridView.Columns[1].Width = pathColWidth;
                    }

                    App.LogService.Trace($"Columns resized - Game: {gameColWidth}, Path: {pathColWidth}, Default: {defaultColWidth}");
                }
            }, "Resize grid view columns");
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (parent == null) return null;

                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                    if (child is T typedChild)
                    {
                        return typedChild;
                    }

                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }

                return null;
            }, $"Find visual child of type {typeof(T).Name}", defaultValue: null);
        }

        private void RemoveColumnHeaderHoverEffects()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug("Removing column header hover effects");

                if (GameListView.View is GridView gridView)
                {
                    Style noHoverStyle = new Style(typeof(GridViewColumnHeader));
                    noHoverStyle.BasedOn = (Style)FindResource("GridViewColumnHeaderStyle");

                    Trigger hoverTrigger = new Trigger
                    {
                        Property = UIElement.IsMouseOverProperty,
                        Value = true
                    };

                    hoverTrigger.Setters.Add(new Setter(
                        Control.BackgroundProperty,
                        FindResource("SecondaryBrush")));

                    hoverTrigger.Setters.Add(new Setter(
                        FrameworkElement.CursorProperty,
                        Cursors.Arrow));

                    noHoverStyle.Triggers.Add(hoverTrigger);

                    foreach (var column in gridView.Columns)
                    {
                        if (column.HeaderContainerStyle == null)
                        {
                            column.HeaderContainerStyle = noHoverStyle;
                        }
                        else
                        {
                            Style mergedStyle = new Style(typeof(GridViewColumnHeader));
                            mergedStyle.BasedOn = column.HeaderContainerStyle;
                            mergedStyle.Triggers.Add(hoverTrigger);
                            column.HeaderContainerStyle = mergedStyle;
                        }
                    }

                    gridView.ColumnHeaderContainerStyle = noHoverStyle;

                    App.LogService.LogDebug("Column header hover effects removed successfully");
                }
            }, "Remove column header hover effects");
        }

        private void SetupContextMenuBehavior()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug("Setting up context menu behavior");

                GameListView.ContextMenuOpening += GameListView_ContextMenuOpening;
            }, "Setup context menu behavior");
        }

        private void GameListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.Trace("Context menu opening event");

                var position = Mouse.GetPosition(GameListView);

                HitTestResult result = VisualTreeHelper.HitTest(GameListView, position);

                if (result == null)
                {
                    App.LogService.Trace("No element hit, suppressing context menu");
                    e.Handled = true;
                    return;
                }

                DependencyObject current = result.VisualHit;
                ListViewItem? listViewItem = null;

                while (current != null && listViewItem == null && current != GameListView)
                {
                    if (current is ListViewItem item)
                    {
                        listViewItem = item;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }

                if (listViewItem == null)
                {
                    App.LogService.Trace("No ListViewItem hit, suppressing context menu");
                    e.Handled = true;
                    return;
                }

                listViewItem.IsSelected = true;
                App.LogService.Trace("ListViewItem found, allowing context menu");
            }, "Context menu opening handler");
        }
    }
}
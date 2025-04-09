using AMO_Launcher.Models;
using AMO_Launcher.Services;
using AMO_Launcher.Utilities;  // Added for ErrorHandler
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;  // Added for Stopwatch
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

        // Property to store the selected game when dialog returns - make nullable
        public GameInfo? SelectedGame { get; private set; }

        public GameSelectionWindow(GameDetectionService gameDetectionService, ConfigurationService configService)
        {
            // Initialize readonly fields in constructor directly
            _gameDetectionService = gameDetectionService;
            _configService = configService;
            _manualGameIconService = App.ManualGameIconService;
            _games = new ObservableCollection<GameInfo>();

            // Wrap the rest of initialization in ErrorHandler
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("Initializing GameSelectionWindow");

                InitializeComponent();
                RemoveColumnHeaderHoverEffects();
                SetupContextMenuBehavior();

                GameListView.ItemsSource = _games;

                // Use ErrorHandler for event handlers
                GameListView.SizeChanged += (s, e) => ErrorHandler.ExecuteSafe(() => ResizeGridViewColumn(),
                    "Resize ListView columns");

                Loaded += (s, e) => ErrorHandler.ExecuteSafe(() => ResizeGridViewColumn(),
                    "Initial column resize");

                // Disable buttons until a game is selected
                SelectButton.IsEnabled = false;
                MakeDefaultButton.IsEnabled = false;
                RemoveGameButton.IsEnabled = false;

                // Enable buttons when a game is selected
                GameListView.SelectionChanged += (s, e) => ErrorHandler.ExecuteSafe(() =>
                {
                    bool hasSelection = GameListView.SelectedItem != null;
                    SelectButton.IsEnabled = hasSelection;
                    MakeDefaultButton.IsEnabled = hasSelection;
                    RemoveGameButton.IsEnabled = hasSelection;

                    // Log selection
                    if (hasSelection && GameListView.SelectedItem is GameInfo selectedGame)
                    {
                        App.LogService?.LogDebug($"UI: Game selected: {selectedGame.Name}");
                    }
                }, "Handle selection change");

                // Double-click to select game
                GameListView.MouseDoubleClick += (s, e) => ErrorHandler.ExecuteSafe(() =>
                {
                    // Only process double-clicks if:
                    // 1. The original source is a UI element within a ListViewItem (not column headers, etc.)
                    // 2. There's a selected item
                    if (GameListView.SelectedItem != null)
                    {
                        // Get the clicked element
                        var originalSource = e.OriginalSource as DependencyObject;

                        // If not a UI element, exit
                        if (originalSource == null)
                            return;

                        // Check if the click was on an actual list item by walking up the visual tree
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

                        // Only process the selection if clicked on an item
                        if (clickedOnItem)
                        {
                            App.LogService?.LogDebug("UI: Double-click selection");
                            SelectButton_Click(this, new RoutedEventArgs());
                            e.Handled = true; // Mark as handled to prevent further processing
                        }
                    }
                }, "Handle double-click selection");

                // Load games when window opens
                Loaded += async (s, e) => await ErrorHandler.ExecuteSafeAsync(async () =>
                {
                    await LoadGamesAsync();
                }, "Load games on window initialization");

                App.LogService?.LogDebug("GameSelectionWindow initialization completed");
            }, "Initialize GameSelectionWindow");
        }

        #region Custom Title Bar Handlers

        // Handle window dragging
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace("UI: Title bar drag initiated");
                this.DragMove();
            }, "Window drag operation");
        }

        // Handle close button click
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

        // Load games from settings and scan for new ones
        private async Task LoadGamesAsync()
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                // Performance tracking for the entire operation
                var operationStopwatch = Stopwatch.StartNew();
                App.LogService.Info("Starting game list loading");

                // Show loading indicator
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    // Clear current games
                    _games.Clear();
                    App.LogService.LogDebug("Cleared existing game list");

                    // Load settings
                    var settings = await _configService.LoadSettingsAsync();
                    App.LogService.LogDebug($"Loaded settings with {settings.Games.Count} saved games");

                    // Scan for games
                    App.LogService.Info("Scanning for installed games");
                    var scanStopwatch = Stopwatch.StartNew();
                    var detectedGames = await _gameDetectionService.ScanForGamesAsync();
                    scanStopwatch.Stop();
                    App.LogService.LogDebug($"Game detection completed in {scanStopwatch.ElapsedMilliseconds}ms, found {detectedGames.Count} games");

                    // Create a new list to merge detected and saved games
                    var mergedGames = new List<GameInfo>();

                    // First add all detected games
                    mergedGames.AddRange(detectedGames);

                    // Then check if any saved games weren't detected and are still valid
                    int restoredGameCount = 0;
                    foreach (var savedGame in settings.Games)
                    {
                        // Skip if this game was already detected
                        if (mergedGames.Any(g => g.ExecutablePath == savedGame.ExecutablePath))
                        {
                            App.LogService.Trace($"Skipping already detected game: {savedGame.Name}");
                            continue;
                        }

                        // Skip if this game is in the removed list
                        if (_configService.IsGameRemoved(savedGame.ExecutablePath))
                        {
                            App.LogService.LogDebug($"Skipping removed game: {savedGame.Name}");
                            continue;
                        }

                        // If the executable still exists, add it to the list
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
                                IsManuallyAdded = savedGame.IsManuallyAdded // Preserve manually added flag
                            };

                            // If it's a manually added game, get the icon from the specialized service
                            if (savedGame.IsManuallyAdded)
                            {
                                gameInfo.Icon = _manualGameIconService.GetIconForGame(savedGame.ExecutablePath);
                                App.LogService.LogDebug($"Loaded icon for manually added game: {savedGame.Name}");
                            }
                            else
                            {
                                // For regular games, use the normal icon extraction
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

                    // Now update the default flag based on settings
                    foreach (var game in mergedGames)
                    {
                        // Check if this game exists in settings
                        var savedGame = settings.Games.FirstOrDefault(g => g.ExecutablePath == game.ExecutablePath);
                        if (savedGame != null)
                        {
                            // Update with saved setting
                            game.IsDefault = savedGame.IsDefault;
                            game.IsManuallyAdded = savedGame.IsManuallyAdded; // Preserve manually added flag

                            // Ensure install directory is set
                            if (string.IsNullOrEmpty(game.InstallDirectory))
                            {
                                game.InstallDirectory = savedGame.InstallDirectory ??
                                                   Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty;
                            }
                        }

                        // Make sure manually added games have icons
                        if (game.IsManuallyAdded && game.Icon == null)
                        {
                            game.Icon = _manualGameIconService.GetIconForGame(game.ExecutablePath);
                            App.LogService.LogDebug($"Loaded missing icon for manually added game: {game.Name}");
                        }
                        // Ensure icons are loaded for regular games
                        else if (!game.IsManuallyAdded && game.Icon == null)
                        {
                            game.Icon = TryExtractIcon(game.ExecutablePath);
                        }

                        // Ensure ID is generated
                        if (string.IsNullOrEmpty(game.Id))
                        {
                            game.GenerateId();
                            App.LogService.LogDebug($"Generated new ID for game: {game.Name}");
                        }
                    }

                    // Sort by name
                    var sortedGames = mergedGames.OrderBy(g => g.Name).ToList();
                    App.LogService.LogDebug($"Sorted {sortedGames.Count} games alphabetically");

                    // Clear and add to observable collection
                    _games.Clear();
                    foreach (var game in sortedGames)
                    {
                        _games.Add(game);
                    }

                    // Update settings with current games and save immediately
                    _configService.UpdateGames(_games.ToList());
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Updated and saved game list to settings");

                    // Select the default or most recently used game
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
                        // Select first game if no preferred game
                        GameListView.SelectedItem = _games[0];
                        App.LogService.LogDebug($"No preferred game, selected first game: {_games[0].Name}");
                    }

                    // Force UI refresh to ensure all bindings are updated
                    GameListView.Items.Refresh();

                    // Log completion with performance metrics
                    operationStopwatch.Stop();
                    App.LogService.Info($"Game list loading completed in {operationStopwatch.ElapsedMilliseconds}ms - Loaded {_games.Count} games");
                }
                finally
                {
                    // Remove loading indicator
                    Mouse.OverrideCursor = null;
                }
            }, "Load games async operation", showErrorToUser: true);
        }

        // Helper method to extract icon from executable and ensure it's properly loaded in the UI
        private BitmapImage? TryExtractIcon(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService.LogDebug($"Cannot extract icon - invalid path or file doesn't exist: {executablePath}");
                    return null;
                }

                // Create a default icon in case extraction fails
                BitmapImage defaultIcon = new BitmapImage();
                defaultIcon.BeginInit();
                defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                defaultIcon.EndInit();

                try
                {
                    App.LogService.Trace($"Extracting icon from: {Path.GetFileName(executablePath)}");

                    // Extract icon directly to byte array to avoid stream disposal issues
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
                            // Save as PNG (better quality than BMP)
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            iconData = stream.ToArray();
                            App.LogService.Trace($"Extracted icon size: {iconData.Length} bytes");
                        }
                    }

                    // Create bitmap image from byte array
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = new MemoryStream(iconData);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it thread-safe

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

        // Scan for games button click handler
        private async void ScanGamesButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService.Info("Manual game scan initiated by user");

                // Disable the button to prevent multiple clicks during scan
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
                    // Re-enable the button and remove wait cursor
                    ScanGamesButton.IsEnabled = true;
                    Mouse.OverrideCursor = null;
                }
            }, "Manual game scan operation", showErrorToUser: true);
        }

        // Add game manually button click handler
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

                            // Flag this as a manually added game
                            gameInfo.IsManuallyAdded = true;

                            // Ensure Icon and InstallDirectory are set
                            if (gameInfo.Icon == null)
                            {
                                // For manually added games, get the icon from the special service
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
                                // If we already have an icon, save it to our manual game icon service
                                _manualGameIconService.SaveIconForGame(gameInfo.ExecutablePath, gameInfo.Icon);
                                App.LogService.LogDebug($"Saved icon for new manually added game: {gameInfo.Name}");
                            }

                            if (string.IsNullOrEmpty(gameInfo.InstallDirectory))
                            {
                                gameInfo.InstallDirectory = Path.GetDirectoryName(gameInfo.ExecutablePath) ?? string.Empty;
                                App.LogService.LogDebug($"Set install directory to: {gameInfo.InstallDirectory}");
                            }

                            // Check if this game already exists
                            var existingGame = _games.FirstOrDefault(g => g.ExecutablePath == gameInfo.ExecutablePath);
                            if (existingGame != null)
                            {
                                App.LogService.Info($"Game already exists in list: {existingGame.Name}");
                                MessageBox.Show("This game is already in your list.", "Game Already Added",
                                    MessageBoxButton.OK, MessageBoxImage.Information);

                                // Select the existing game
                                GameListView.SelectedItem = existingGame;
                            }
                            else
                            {
                                // Add the new game and select it
                                _games.Add(gameInfo);
                                GameListView.SelectedItem = gameInfo;

                                // Update settings and save immediately
                                _configService.UpdateGames(_games.ToList());
                                await _configService.SaveSettingsAsync();

                                // Refresh UI
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

        // Remove game button click handler
        private async void RemoveGameButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.LogDebug($"Attempting to remove game: {selectedGame.Name}");

                    // Confirm with user
                    var result = MessageBox.Show(
                        $"Are you sure you want to remove '{selectedGame.Name}' from the list?\n\nThis will not uninstall the game or delete any files.",
                        "Confirm Removal",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.LogService.Info($"Removing game: {selectedGame.Name}");

                        // Add the game to the removed list
                        _configService.AddToRemovedGames(selectedGame.ExecutablePath);
                        App.LogService.LogDebug($"Added to removed games list: {selectedGame.ExecutablePath}");

                        // Remove from the list
                        _games.Remove(selectedGame);

                        // Update configuration
                        _configService.UpdateGames(_games.ToList());

                        // If the removed game was the default, clear the default setting
                        if (selectedGame.IsDefault)
                        {
                            App.LogService.LogDebug("Removed game was the default, clearing default setting");
                            _configService.SetDefaultGame(string.Empty); // Use empty string instead of null
                        }

                        // Save changes
                        await _configService.SaveSettingsAsync();
                        App.LogService.LogDebug("Saved settings after game removal");

                        // Refresh the list view
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

        // Make default button click handler
        private async void MakeDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.Info($"Setting game as default: {selectedGame.Name}");

                    // Update all games to not be default
                    foreach (var game in _games)
                    {
                        game.IsDefault = false;
                    }

                    // Set the selected game as default
                    selectedGame.IsDefault = true;
                    App.LogService.LogDebug($"Updated IsDefault flag for: {selectedGame.Name}");

                    // Update configuration
                    _configService.SetDefaultGame(selectedGame.Id);
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Saved default game setting");

                    // Refresh the list view to update the checkboxes
                    GameListView.Items.Refresh();

                    App.LogService.Info($"Default game set successfully: {selectedGame.Name}");
                    MessageBox.Show($"{selectedGame.Name} has been set as the default game.",
                        "Default Game Set", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, "Set default game operation", showErrorToUser: true);
        }

        // Default checkbox click handler
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

                    // Find the game
                    var selectedGame = _games.FirstOrDefault(g => g.Id == gameId);
                    if (selectedGame == null)
                    {
                        App.LogService.Warning($"DefaultCheckBox_Click: Game with ID {gameId} not found");
                        return;
                    }

                    App.LogService.Info($"Setting game as default via checkbox: {selectedGame.Name}");

                    // Uncheck all other checkboxes
                    foreach (var game in _games)
                    {
                        game.IsDefault = game.Id == gameId;
                    }

                    // Update configuration
                    _configService.SetDefaultGame(gameId);
                    await _configService.SaveSettingsAsync();
                    App.LogService.LogDebug("Saved default game setting via checkbox");

                    // Refresh the list view
                    GameListView.Items.Refresh();

                    App.LogService.Info($"Default game set successfully via checkbox: {selectedGame.Name}");
                }
            }, "Default checkbox click operation");
        }

        // Context menu handler for copying path to clipboard
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
                    // Removed the popup message box
                }
                else
                {
                    App.LogService.Warning("Cannot copy path - no game selected or path is empty");
                }
            }, "Copy path to clipboard operation");
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug("Cancel button clicked");
                DialogResult = false;
                Close();
            }, "Cancel operation");
        }

        // Select button click handler
        private async void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var selectedGame = GameListView.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    App.LogService.Info($"Game selected: {selectedGame.Name}");
                    SelectedGame = selectedGame;

                    // Only save as last selected if the setting is enabled
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

        // Resize grid view columns
        private void ResizeGridViewColumn()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (GameListView.View is GridView gridView)
                {
                    App.LogService.Trace("Resizing grid view columns");

                    // Get the usable width of the ListView (account for border, padding, etc.)
                    double actualWidth = GameListView.ActualWidth;

                    // Fixed widths for first and last columns
                    double gameColWidth = 200; // Game column width
                    double defaultColWidth = 60; // Default column width

                    // Check if vertical scrollbar is visible and account for it
                    bool hasVerticalScrollBar = false;
                    var scrollViewer = FindVisualChild<ScrollViewer>(GameListView);
                    if (scrollViewer != null)
                    {
                        hasVerticalScrollBar = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
                    }

                    // Calculate scrollbar width if needed
                    double scrollBarWidth = hasVerticalScrollBar ? SystemParameters.VerticalScrollBarWidth : 0;

                    // Set fixed widths for first and last columns
                    gridView.Columns[0].Width = gameColWidth;
                    gridView.Columns[2].Width = defaultColWidth;

                    // Calculate the remaining width for the middle column
                    double pathColWidth = actualWidth - gameColWidth - defaultColWidth - scrollBarWidth - 2; // 2 for borders

                    // Ensure the middle column has a reasonable minimum width
                    if (pathColWidth > 50)
                    {
                        gridView.Columns[1].Width = pathColWidth;
                    }

                    App.LogService.Trace($"Columns resized - Game: {gameColWidth}, Path: {pathColWidth}, Default: {defaultColWidth}");
                }
            }, "Resize grid view columns");
        }

        // Helper method to find visual child of type T
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

                // Find the GridView inside the ListView
                if (GameListView.View is GridView gridView)
                {
                    // Create a style to override the default hover behavior
                    Style noHoverStyle = new Style(typeof(GridViewColumnHeader));
                    noHoverStyle.BasedOn = (Style)FindResource("GridViewColumnHeaderStyle");

                    // Add a trigger that ensures no visual change on hover
                    Trigger hoverTrigger = new Trigger
                    {
                        Property = UIElement.IsMouseOverProperty,
                        Value = true
                    };

                    // Set the background to remain the same on hover
                    hoverTrigger.Setters.Add(new Setter(
                        Control.BackgroundProperty,
                        FindResource("SecondaryBrush")));

                    // Ensure cursor stays as an arrow and doesn't change to a resize cursor
                    hoverTrigger.Setters.Add(new Setter(
                        FrameworkElement.CursorProperty,
                        Cursors.Arrow));

                    noHoverStyle.Triggers.Add(hoverTrigger);

                    // Apply the style to each column header
                    foreach (var column in gridView.Columns)
                    {
                        if (column.HeaderContainerStyle == null)
                        {
                            column.HeaderContainerStyle = noHoverStyle;
                        }
                        else
                        {
                            // If the column already has a header style, we need to merge in our trigger
                            Style mergedStyle = new Style(typeof(GridViewColumnHeader));
                            mergedStyle.BasedOn = column.HeaderContainerStyle;
                            mergedStyle.Triggers.Add(hoverTrigger);
                            column.HeaderContainerStyle = mergedStyle;
                        }
                    }

                    // Also set the style for any new columns that might be added
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

                // Add event handler to control when the context menu should open
                GameListView.ContextMenuOpening += GameListView_ContextMenuOpening;
            }, "Setup context menu behavior");
        }

        // Add this event handler to control when context menu appears
        private void GameListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.Trace("Context menu opening event");

                // Get the position of the mouse relative to the ListView
                var position = Mouse.GetPosition(GameListView);

                // Use hit testing to determine if the mouse is over an item
                HitTestResult result = VisualTreeHelper.HitTest(GameListView, position);

                if (result == null)
                {
                    // No element was hit - don't show context menu
                    App.LogService.Trace("No element hit, suppressing context menu");
                    e.Handled = true;
                    return;
                }

                // Walk up the visual tree to find a ListViewItem (if any)
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

                // If we didn't hit a ListViewItem, don't show context menu
                if (listViewItem == null)
                {
                    App.LogService.Trace("No ListViewItem hit, suppressing context menu");
                    e.Handled = true;
                    return;
                }

                // ListViewItem found - allow context menu to open
                // Make sure the item is selected (for visual feedback)
                listViewItem.IsSelected = true;
                App.LogService.Trace("ListViewItem found, allowing context menu");
            }, "Context menu opening handler");
        }
    }
}
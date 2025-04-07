using AMO_Launcher.Models;
using AMO_Launcher.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            InitializeComponent();
            RemoveColumnHeaderHoverEffects();
            SetupContextMenuBehavior();

            _gameDetectionService = gameDetectionService;
            _configService = configService;
            _manualGameIconService = App.ManualGameIconService;
            _games = new ObservableCollection<GameInfo>();

            GameListView.ItemsSource = _games;
            GameListView.SizeChanged += (s, e) => ResizeGridViewColumn();
            Loaded += (s, e) => ResizeGridViewColumn();

            // Disable buttons until a game is selected
            SelectButton.IsEnabled = false;
            MakeDefaultButton.IsEnabled = false;
            RemoveGameButton.IsEnabled = false;

            // Enable buttons when a game is selected
            GameListView.SelectionChanged += (s, e) =>
            {
                bool hasSelection = GameListView.SelectedItem != null;
                SelectButton.IsEnabled = hasSelection;
                MakeDefaultButton.IsEnabled = hasSelection;
                RemoveGameButton.IsEnabled = hasSelection;
            };

            // Double-click to select game
            GameListView.MouseDoubleClick += (s, e) =>
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
                        SelectButton_Click(this, new RoutedEventArgs());
                        e.Handled = true; // Mark as handled to prevent further processing
                    }
                }
            };

            // Load games when window opens
            Loaded += async (s, e) => await LoadGamesAsync();
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
            DialogResult = false;
            Close();
        }

        #endregion

        // Load games from settings and scan for new ones
        private async Task LoadGamesAsync()
        {
            // Show loading indicator
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Clear current games
                _games.Clear();

                // Load settings
                var settings = await _configService.LoadSettingsAsync();

                // Scan for games
                var detectedGames = await _gameDetectionService.ScanForGamesAsync();

                // Create a new list to merge detected and saved games
                var mergedGames = new List<GameInfo>();

                // First add all detected games
                mergedGames.AddRange(detectedGames);

                // Then check if any saved games weren't detected and are still valid
                foreach (var savedGame in settings.Games)
                {
                    // Skip if this game was already detected
                    if (mergedGames.Any(g => g.ExecutablePath == savedGame.ExecutablePath))
                    {
                        continue;
                    }

                    // Skip if this game is in the removed list
                    if (_configService.IsGameRemoved(savedGame.ExecutablePath))
                    {
                        continue;
                    }

                    // If the executable still exists, add it to the list
                    if (_gameDetectionService.GameExistsOnDisk(savedGame.ExecutablePath))
                    {
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
                            System.Diagnostics.Debug.WriteLine($"Loaded icon for manually added game: {savedGame.Name} ({savedGame.ExecutablePath})");
                        }
                        else
                        {
                            // For regular games, use the normal icon extraction
                            gameInfo.Icon = TryExtractIcon(savedGame.ExecutablePath);
                        }

                        mergedGames.Add(gameInfo);
                    }
                }

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
                        System.Diagnostics.Debug.WriteLine($"Loaded missing icon for manually added game: {game.Name}");
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
                    }
                }

                // Sort by name
                var sortedGames = mergedGames.OrderBy(g => g.Name).ToList();

                // Clear and add to observable collection
                _games.Clear();
                foreach (var game in sortedGames)
                {
                    _games.Add(game);
                }

                // Update settings with current games and save immediately
                _configService.UpdateGames(_games.ToList());
                await _configService.SaveSettingsAsync();

                // Select the default or most recently used game
                string preferredGameId = _configService.GetPreferredGameId();
                if (!string.IsNullOrEmpty(preferredGameId))
                {
                    var gameToSelect = _games.FirstOrDefault(g => g.Id == preferredGameId);
                    if (gameToSelect != null)
                    {
                        GameListView.SelectedItem = gameToSelect;
                    }
                }
                else if (_games.Count > 0)
                {
                    // Select first game if no preferred game
                    GameListView.SelectedItem = _games[0];
                }

                // Force UI refresh to ensure all bindings are updated
                GameListView.Items.Refresh();
            }
            finally
            {
                // Remove loading indicator
                Mouse.OverrideCursor = null;
            }
        }

        // Helper method to extract icon from executable and ensure it's properly loaded in the UI
        private BitmapImage? TryExtractIcon(string executablePath)
        {
            try
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                    return null;

                // Create a default icon in case extraction fails
                BitmapImage defaultIcon = new BitmapImage();
                defaultIcon.BeginInit();
                defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                defaultIcon.EndInit();

                try
                {
                    // Extract icon directly to byte array to avoid stream disposal issues
                    byte[] iconData;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath))
                    {
                        if (icon == null)
                            return defaultIcon;

                        using (var bitmap = icon.ToBitmap())
                        using (var stream = new MemoryStream())
                        {
                            // Save as PNG (better quality than BMP)
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            iconData = stream.ToArray();
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

                    return bitmapImage;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Icon extraction error: {ex.Message}");
                    return defaultIcon;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"General error extracting icon: {ex.Message}");
                return null;
            }
        }

        // Scan for games button click handler
        private async void ScanGamesButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable the button to prevent multiple clicks during scan
            ScanGamesButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                await LoadGamesAsync();
                MessageBox.Show("Game scan completed.", "Scan Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning for games: {ex.Message}", "Scan Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable the button and remove wait cursor
                ScanGamesButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        // Add game manually button click handler
        private async void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select F1 Game Executable",
                Filter = "Game Executable (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    var gameInfo = _gameDetectionService.AddGameManually(openFileDialog.FileName);
                    if (gameInfo != null)
                    {
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
                                System.Diagnostics.Debug.WriteLine($"Got icon for new manually added game: {gameInfo.Name}");
                            }
                        }
                        else
                        {
                            // If we already have an icon, save it to our manual game icon service
                            _manualGameIconService.SaveIconForGame(gameInfo.ExecutablePath, gameInfo.Icon);
                            System.Diagnostics.Debug.WriteLine($"Saved icon for new manually added game: {gameInfo.Name}");
                        }

                        if (string.IsNullOrEmpty(gameInfo.InstallDirectory))
                        {
                            gameInfo.InstallDirectory = Path.GetDirectoryName(gameInfo.ExecutablePath) ?? string.Empty;
                        }

                        // Check if this game already exists
                        var existingGame = _games.FirstOrDefault(g => g.ExecutablePath == gameInfo.ExecutablePath);
                        if (existingGame != null)
                        {
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

                            MessageBox.Show($"{gameInfo.Name} has been added to your game list.", "Game Added",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show("The selected file doesn't appear to be a valid game or cannot be added.",
                            "Invalid Game", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error adding game: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        // Remove game button click handler
        private async void RemoveGameButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedGame = GameListView.SelectedItem as GameInfo;
            if (selectedGame != null)
            {
                // Confirm with user
                var result = MessageBox.Show(
                    $"Are you sure you want to remove '{selectedGame.Name}' from the list?\n\nThis will not uninstall the game or delete any files.",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Add the game to the removed list
                    _configService.AddToRemovedGames(selectedGame.ExecutablePath);

                    // Remove from the list
                    _games.Remove(selectedGame);

                    // Update configuration
                    _configService.UpdateGames(_games.ToList());

                    // If the removed game was the default, clear the default setting
                    if (selectedGame.IsDefault)
                    {
                        _configService.SetDefaultGame(string.Empty); // Use empty string instead of null
                    }

                    // Save changes
                    await _configService.SaveSettingsAsync();

                    // Refresh the list view
                    GameListView.Items.Refresh();

                    MessageBox.Show($"{selectedGame.Name} has been removed from your game list.",
                        "Game Removed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // Make default button click handler
        private async void MakeDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedGame = GameListView.SelectedItem as GameInfo;
            if (selectedGame != null)
            {
                // Update all games to not be default
                foreach (var game in _games)
                {
                    game.IsDefault = false;
                }

                // Set the selected game as default
                selectedGame.IsDefault = true;

                // Update configuration
                _configService.SetDefaultGame(selectedGame.Id);
                await _configService.SaveSettingsAsync();

                // Refresh the list view to update the checkboxes
                GameListView.Items.Refresh();

                MessageBox.Show($"{selectedGame.Name} has been set as the default game.",
                    "Default Game Set", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Default checkbox click handler
        private async void DefaultCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox != null && checkBox.Tag != null)
            {
                string gameId = checkBox.Tag.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(gameId)) return;

                // Find the game
                var selectedGame = _games.FirstOrDefault(g => g.Id == gameId);
                if (selectedGame == null) return;

                // Uncheck all other checkboxes
                foreach (var game in _games)
                {
                    game.IsDefault = game.Id == gameId;
                }

                // Update configuration
                _configService.SetDefaultGame(gameId);
                await _configService.SaveSettingsAsync();

                // Refresh the list view
                GameListView.Items.Refresh();
            }
        }

        // Context menu handler for copying path to clipboard
        private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedGame = GameListView.SelectedItem as GameInfo;
            if (selectedGame != null && !string.IsNullOrEmpty(selectedGame.ExecutablePath))
            {
                try
                {
                    Clipboard.SetText(selectedGame.ExecutablePath);
                    // Removed the popup message box
                    // MessageBox.Show("Game path copied to clipboard.", "Path Copied",
                    //     MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying path: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Select button click handler
        private async void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedGame = GameListView.SelectedItem as GameInfo;
            if (selectedGame != null)
            {
                SelectedGame = selectedGame;

                // Save this as the last selected game
                _configService.SetLastSelectedGame(selectedGame.Id);
                await _configService.SaveSettingsAsync();

                DialogResult = true;
                Close();
            }
        }

        // Add this to your class
        private void ResizeGridViewColumn()
        {
            if (GameListView.View is GridView gridView)
            {
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
            }
        }

        // Helper method to find visual child of type T
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
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
        }

        private void RemoveColumnHeaderHoverEffects()
        {
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
            }
        }

        private void SetupContextMenuBehavior()
        {
            // Add event handler to control when the context menu should open
            GameListView.ContextMenuOpening += GameListView_ContextMenuOpening;
        }

        // Add this event handler to control when context menu appears
        private void GameListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Get the position of the mouse relative to the ListView
            var position = Mouse.GetPosition(GameListView);

            // Use hit testing to determine if the mouse is over an item
            HitTestResult result = VisualTreeHelper.HitTest(GameListView, position);

            if (result == null)
            {
                // No element was hit - don't show context menu
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
                e.Handled = true;
                return;
            }

            // ListViewItem found - allow context menu to open
            // Make sure the item is selected (for visual feedback)
            listViewItem.IsSelected = true;
        }
    }
}
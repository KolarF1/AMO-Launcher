using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Views
{
    public partial class UpdateAvailableDialog : Window
    {
        public bool InstallNow { get; private set; }
        private Version _currentVersion;
        private Version _newVersion;

        public UpdateAvailableDialog(Version currentVersion, Version newVersion, string releaseNotes)
        {
            _currentVersion = currentVersion;
            _newVersion = newVersion;

            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"Showing update dialog for new version {newVersion}");

                InitializeComponent();

                CurrentVersionTextBlock.Text = currentVersion.ToString();
                NewVersionTextBlock.Text = newVersion.ToString();

                if (!string.IsNullOrEmpty(releaseNotes))
                {
                    ReleaseNotesTextBox.Text = releaseNotes;
                    App.LogService?.LogDebug($"Update includes release notes ({releaseNotes.Length} characters)");
                }
                else
                {
                    ReleaseNotesTextBox.Text = "No release notes available.";
                    App.LogService?.LogDebug("No release notes available for this update");
                }

                App.LogService?.LogDebug("Update dialog initialized successfully");
            }, "Initialize update dialog", true);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Trace("User dragging update dialog window");
                this.DragMove();
            }, "Move update dialog window", false);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info("User closed update dialog via close button");
                InstallNow = false;
                DialogResult = false;
                Close();
            }, "Close update dialog", true);
        }

        private void InstallNowButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"User chose to install update from v{_currentVersion} to v{_newVersion} now");
                InstallNow = true;
                DialogResult = true;
                Close();
            }, "Process install now button click", true);
        }

        private void RemindLaterButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"User chose to be reminded later about update from v{_currentVersion} to v{_newVersion}");
                InstallNow = false;
                DialogResult = false;
                Close();
            }, "Process remind later button click", true);
        }
    }
}
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AMO_Launcher.Views
{
    public partial class UpdateAvailableDialog : Window
    {
        public bool InstallNow { get; private set; }

        public UpdateAvailableDialog(Version currentVersion, Version newVersion, string releaseNotes)
        {
            InitializeComponent();

            CurrentVersionTextBlock.Text = currentVersion.ToString();
            NewVersionTextBlock.Text = newVersion.ToString();

            if (!string.IsNullOrEmpty(releaseNotes))
            {
                ReleaseNotesTextBox.Text = releaseNotes;
            }
            else
            {
                ReleaseNotesTextBox.Text = "No release notes available.";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            InstallNow = false;
            DialogResult = false;
            Close();
        }

        private void InstallNowButton_Click(object sender, RoutedEventArgs e)
        {
            InstallNow = true;
            DialogResult = true;
            Close();
        }

        private void RemindLaterButton_Click(object sender, RoutedEventArgs e)
        {
            InstallNow = false;
            DialogResult = false;
            Close();
        }
    }
}
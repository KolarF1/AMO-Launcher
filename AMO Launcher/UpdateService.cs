using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AMO_Launcher.Services
{
    public class UpdateService
    {
        private readonly string _owner;
        private readonly string _repo;
        private readonly HttpClient _httpClient;
        private readonly Version _currentVersion;

        private readonly string _updateTempFolder;
        private readonly string _updaterExePath;

        public bool IsCheckingForUpdates { get; private set; }
        public event EventHandler<UpdateAvailableEventArgs> UpdateAvailable;
        public event EventHandler<Exception> UpdateCheckFailed;

        public UpdateService(string owner, string repo)
        {
            _owner = owner;
            _repo = repo;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("AMO-Launcher", "1.0"));

            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            // Set up paths for updates
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            _updateTempFolder = Path.Combine(appDataPath, "Updates");
            _updaterExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AMO_Updater.exe");

            // Create update folder if it doesn't exist
            if (!Directory.Exists(_updateTempFolder))
            {
                Directory.CreateDirectory(_updateTempFolder);
            }

            App.LogToFile($"Update service initialized for {_owner}/{_repo}, current version: {_currentVersion}");
        }

        public async Task CheckForUpdatesAsync(bool silent = false)
        {
            if (IsCheckingForUpdates)
                return;

            try
            {
                IsCheckingForUpdates = true;
                App.LogToFile("Checking for updates...");

                // Clear any previous update files
                ClearUpdateFolder();

                // Get latest release from GitHub
                var latestRelease = await GetLatestReleaseAsync();
                if (latestRelease == null)
                {
                    App.LogToFile("No releases found on GitHub");
                    return;
                }

                // Parse release version (tag name format should be v1.2.3 or similar)
                string tagName = latestRelease.TagName.TrimStart('v');
                if (!Version.TryParse(tagName, out Version latestVersion))
                {
                    App.LogToFile($"Invalid version format in tag: {latestRelease.TagName}");
                    return;
                }

                App.LogToFile($"Latest version: {latestVersion}, Current version: {_currentVersion}");

                // Check if update is needed
                if (latestVersion > _currentVersion)
                {
                    App.LogToFile("Update available!");

                    // Look for the asset that contains our update package
                    string updateAssetUrl = null;
                    foreach (var asset in latestRelease.Assets)
                    {
                        if (asset.Name.EndsWith(".zip") && asset.Name.StartsWith("AMO_Launcher"))
                        {
                            updateAssetUrl = asset.BrowserDownloadUrl;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(updateAssetUrl))
                    {
                        App.LogToFile("Update package not found in release assets");
                        return;
                    }

                    // Notify listeners about the update
                    OnUpdateAvailable(new UpdateAvailableEventArgs
                    {
                        CurrentVersion = _currentVersion,
                        NewVersion = latestVersion,
                        ReleaseNotes = latestRelease.Body,
                        DownloadUrl = updateAssetUrl
                    });
                }
                else
                {
                    App.LogToFile("No updates available");
                    if (!silent)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                "You are running the latest version of AMO Launcher.",
                                "No Updates Available",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error checking for updates: {ex.Message}");
                OnUpdateCheckFailed(ex);
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        public async Task<string> DownloadUpdateAsync(string downloadUrl)
        {
            try
            {
                App.LogToFile($"Downloading update from: {downloadUrl}");

                // Create a unique filename for the downloaded zip
                string updateZipPath = Path.Combine(_updateTempFolder, $"update_{DateTime.Now:yyyyMMddHHmmss}.zip");

                // Download the update package
                byte[] zipData = await _httpClient.GetByteArrayAsync(downloadUrl);
                File.WriteAllBytes(updateZipPath, zipData);

                App.LogToFile($"Update downloaded to: {updateZipPath}");
                return updateZipPath;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error downloading update: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> PrepareUpdateAsync(string updateZipPath)
        {
            try
            {
                App.LogToFile($"Preparing update from: {updateZipPath}");

                // Extract the zip to the update folder
                string extractPath = Path.Combine(_updateTempFolder, "ExtractedUpdate");

                // Clear any existing extracted files
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);

                // Extract the update package
                ZipFile.ExtractToDirectory(updateZipPath, extractPath);

                App.LogToFile($"Update extracted to: {extractPath}");
                return true;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error preparing update: {ex.Message}");
                return false;
            }
        }

        public bool ApplyUpdate(string extractedUpdatePath)
        {
            try
            {
                App.LogToFile("Applying update...");

                if (!File.Exists(_updaterExePath))
                {
                    App.LogToFile($"Updater executable not found at: {_updaterExePath}");
                    throw new FileNotFoundException("Updater executable not found", _updaterExePath);
                }

                // Get the main application path and process ID
                string appPath = Assembly.GetExecutingAssembly().Location;
                int currentProcessId = Process.GetCurrentProcess().Id;

                // Launch the updater with the necessary parameters
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _updaterExePath,
                    Arguments = $"\"{extractedUpdatePath}\" \"{appPath}\" {currentProcessId}",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                App.LogToFile($"Launching updater: {_updaterExePath} with args: {startInfo.Arguments}");
                Process.Start(startInfo);

                return true;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error applying update: {ex.Message}");
                return false;
            }
        }

        private void ClearUpdateFolder()
        {
            try
            {
                if (Directory.Exists(_updateTempFolder))
                {
                    // Delete all .zip files in the update folder
                    foreach (string file in Directory.GetFiles(_updateTempFolder, "*.zip"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            App.LogToFile($"Error deleting file {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error clearing update folder: {ex.Message}");
            }
        }

        private async Task<GitHubRelease> GetLatestReleaseAsync()
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

                App.LogToFile($"Fetching latest release from: {apiUrl}");

                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return release;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error getting latest release: {ex.Message}");
                return null;
            }
        }

        protected virtual void OnUpdateAvailable(UpdateAvailableEventArgs e)
        {
            UpdateAvailable?.Invoke(this, e);
        }

        protected virtual void OnUpdateCheckFailed(Exception ex)
        {
            UpdateCheckFailed?.Invoke(this, ex);
        }

        // Helper classes for JSON deserialization
        private class GitHubRelease
        {
            public string Url { get; set; }
            public string AssetsUrl { get; set; }
            public string HtmlUrl { get; set; }
            public int Id { get; set; }
            public string NodeId { get; set; }
            public string TagName { get; set; }
            public string Name { get; set; }
            public bool Draft { get; set; }
            public bool Prerelease { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime PublishedAt { get; set; }
            public GitHubAsset[] Assets { get; set; }
            public string Body { get; set; }
        }

        private class GitHubAsset
        {
            public string Url { get; set; }
            public int Id { get; set; }
            public string NodeId { get; set; }
            public string Name { get; set; }
            public string ContentType { get; set; }
            public string State { get; set; }
            public long Size { get; set; }
            public string BrowserDownloadUrl { get; set; }
        }
    }

    public class UpdateAvailableEventArgs : EventArgs
    {
        public Version CurrentVersion { get; set; }
        public Version NewVersion { get; set; }
        public string ReleaseNotes { get; set; }
        public string DownloadUrl { get; set; }
    }
}
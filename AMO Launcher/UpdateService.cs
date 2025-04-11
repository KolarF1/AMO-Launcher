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
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Services
{
    public enum UpdateErrorCategory
    {
        Network,
        FileSystem,
        Extraction,
        ApplicationExit,
        External,
        Unknown
    }

    public class UpdateService
    {
        private readonly string _owner;
        private readonly string _repo;
        private readonly HttpClient _httpClient;
        private readonly Version _currentVersion;

        private readonly string _updateTempFolder;
        private readonly string _updaterExePath;

        private readonly int _downloadWarningThresholdMs = 30000;
        private readonly int _extractWarningThresholdMs = 5000;

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

            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            _updateTempFolder = Path.Combine(appDataPath, "Updates");
            _updaterExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AMO_Updater.exe");

            ErrorHandler.ExecuteSafe(() =>
            {
                if (!Directory.Exists(_updateTempFolder))
                {
                    App.LogService?.LogDebug($"Creating update temp folder: {_updateTempFolder}");
                    Directory.CreateDirectory(_updateTempFolder);
                }
            }, "Creating update temp folder", false);

            App.LogService?.Info($"Update service initialized for {_owner}/{_repo}, current version: {_currentVersion}");
        }

        public async Task CheckForUpdatesAsync(bool silent = false)
        {
            if (IsCheckingForUpdates)
            {
                App.LogService?.LogDebug("Update check already in progress, ignoring request");
                return;
            }

            string operationId = $"UpdateCheck_{DateTime.Now:yyyyMMdd_HHmmss}";
            App.LogService?.Info($"[{operationId}] Starting update check");

            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                IsCheckingForUpdates = true;

                try
                {
                    var startTime = DateTime.Now;

                    ClearUpdateFolder();

                    App.LogService?.LogDebug($"[{operationId}] Fetching latest release information");
                    var latestRelease = await GetLatestReleaseAsync();

                    if (latestRelease == null)
                    {
                        App.LogService?.Warning($"[{operationId}] No releases found on GitHub repository {_owner}/{_repo}");
                        return;
                    }

                    string tagName = latestRelease.TagName.TrimStart('v');
                    if (!Version.TryParse(tagName, out Version latestVersion))
                    {
                        App.LogService?.Error($"[{operationId}] Invalid version format in tag: {latestRelease.TagName}");
                        return;
                    }

                    App.LogService?.Info($"[{operationId}] Latest version: {latestVersion}, Current version: {_currentVersion}");

                    if (latestVersion > _currentVersion)
                    {
                        App.LogService?.Info($"[{operationId}] Update available! New version: {latestVersion}");

                        string updateAssetUrl = null;
                        foreach (var asset in latestRelease.Assets)
                        {
                            if (asset.Name.EndsWith(".zip") && asset.Name.StartsWith("AMO_Launcher"))
                            {
                                updateAssetUrl = asset.BrowserDownloadUrl;
                                App.LogService?.LogDebug($"[{operationId}] Found update asset: {asset.Name} ({FormatFileSize(asset.Size)})");
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(updateAssetUrl))
                        {
                            App.LogService?.Error($"[{operationId}] Update package not found in release assets");
                            return;
                        }

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
                        App.LogService?.Info($"[{operationId}] No updates available, current version is up to date");
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

                    var elapsed = DateTime.Now - startTime;
                    App.LogService?.LogDebug($"[{operationId}] Update check completed in {elapsed.TotalSeconds:F2} seconds");
                }
                finally
                {
                    IsCheckingForUpdates = false;
                }
            }, "Checking for updates", showErrorToUser: !silent);
        }

        public async Task<string> DownloadUpdateAsync(string downloadUrl)
        {
            string downloadId = $"Download_{DateTime.Now:yyyyMMdd_HHmmss}";

            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info($"[{downloadId}] Downloading update from: {downloadUrl}");

                var startTime = DateTime.Now;

                string updateZipPath = Path.Combine(_updateTempFolder, $"update_{DateTime.Now:yyyyMMddHHmmss}.zip");

                App.LogService?.LogDebug($"[{downloadId}] Update will be saved to: {updateZipPath}");

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    App.LogService?.LogDebug($"[{downloadId}] Starting download of {(totalBytes.HasValue ? FormatFileSize(totalBytes.Value) : "unknown size")}");

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(updateZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes.HasValue && totalBytesRead % (totalBytes.Value / 10) < 8192)
                            {
                                double progressPercent = (double)totalBytesRead / totalBytes.Value * 100;
                                App.LogService?.LogDebug($"[{downloadId}] Download progress: {progressPercent:F1}% ({FormatFileSize(totalBytesRead)})");
                            }
                        }
                    }
                }

                var elapsed = DateTime.Now - startTime;
                App.LogService?.Info($"[{downloadId}] Update downloaded successfully in {elapsed.TotalSeconds:F1} seconds");

                if (elapsed.TotalMilliseconds > _downloadWarningThresholdMs)
                {
                    App.LogService?.Warning($"[{downloadId}] Download took longer than expected ({elapsed.TotalSeconds:F1} seconds)");
                }

                return updateZipPath;
            }, "Downloading update", true, null);
        }

        public async Task<bool> PrepareUpdateAsync(string updateZipPath)
        {
            string extractId = $"Extract_{DateTime.Now:yyyyMMdd_HHmmss}";

            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                App.LogService?.Info($"[{extractId}] Preparing update from: {updateZipPath}");
                var startTime = DateTime.Now;

                if (!File.Exists(updateZipPath))
                {
                    LogCategorizedError($"Update zip file not found: {updateZipPath}", null, UpdateErrorCategory.FileSystem);
                    return false;
                }

                string extractPath = Path.Combine(_updateTempFolder, "ExtractedUpdate");

                if (Directory.Exists(extractPath))
                {
                    App.LogService?.LogDebug($"[{extractId}] Removing existing extracted files");
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);
                App.LogService?.LogDebug($"[{extractId}] Created extraction directory: {extractPath}");

                App.LogService?.LogDebug($"[{extractId}] Extracting update package");
                ZipFile.ExtractToDirectory(updateZipPath, extractPath);

                var elapsed = DateTime.Now - startTime;
                App.LogService?.Info($"[{extractId}] Update extracted successfully in {elapsed.TotalSeconds:F1} seconds");

                if (elapsed.TotalMilliseconds > _extractWarningThresholdMs)
                {
                    App.LogService?.Warning($"[{extractId}] Extraction took longer than expected ({elapsed.TotalSeconds:F1} seconds)");
                }

                await Task.Delay(1);
                return true;
            }, "Extracting update", true, false);
        }

        public bool ApplyUpdate(string extractedUpdatePath)
        {
            string applyId = $"Apply_{DateTime.Now:yyyyMMdd_HHmmss}";

            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.Info($"[{applyId}] Applying update from: {extractedUpdatePath}");

                if (!File.Exists(_updaterExePath))
                {
                    string errorMessage = $"Updater executable not found at: {_updaterExePath}";
                    LogCategorizedError(errorMessage, new FileNotFoundException(errorMessage, _updaterExePath), UpdateErrorCategory.External);
                    return false;
                }

                string appPath = Assembly.GetExecutingAssembly().Location;
                int currentProcessId = Process.GetCurrentProcess().Id;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _updaterExePath,
                    Arguments = $"\"{extractedUpdatePath}\" \"{appPath}\" {currentProcessId}",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                App.LogService?.LogDebug($"[{applyId}] Launching updater: {_updaterExePath}");
                App.LogService?.LogDebug($"[{applyId}] Update source: {extractedUpdatePath}");
                App.LogService?.LogDebug($"[{applyId}] Application target: {appPath}");
                App.LogService?.LogDebug($"[{applyId}] Process ID to terminate: {currentProcessId}");

                Process.Start(startInfo);
                App.LogService?.Info($"[{applyId}] Updater launched successfully, application will restart");

                return true;
            }, "Applying update", true, false);
        }

        private void ClearUpdateFolder()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (Directory.Exists(_updateTempFolder))
                {
                    App.LogService?.LogDebug($"Clearing update folder: {_updateTempFolder}");

                    int deletedCount = 0;
                    foreach (string file in Directory.GetFiles(_updateTempFolder, "*.zip"))
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            App.LogService?.LogDebug($"Error deleting file {file}: {ex.Message}");
                        }
                    }

                    App.LogService?.LogDebug($"Cleared {deletedCount} update files");
                }
            }, "Clearing update folder", false);
        }

        private async Task<GitHubRelease> GetLatestReleaseAsync()
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                string apiUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

                App.LogService?.LogDebug($"Fetching latest release from: {apiUrl}");

                try
                {
                    HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                    App.LogService?.LogDebug($"GitHub API response status: {(int)response.StatusCode} {response.StatusCode}");

                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    App.LogService?.LogDebug($"Successfully parsed release: {release.Name} ({release.TagName})");
                    return release;
                }
                catch (HttpRequestException ex)
                {
                    LogCategorizedError($"GitHub API request failed: {ex.Message}", ex, UpdateErrorCategory.Network);
                    return null;
                }
            }, "Getting latest release from GitHub", false, null);
        }

        protected virtual void OnUpdateAvailable(UpdateAvailableEventArgs e)
        {
            App.LogService?.Info($"Update event: v{e.CurrentVersion} → v{e.NewVersion}");
            UpdateAvailable?.Invoke(this, e);
        }

        protected virtual void OnUpdateCheckFailed(Exception ex)
        {
            App.LogService?.Error($"Update check failed: {ex.Message}");
            UpdateCheckFailed?.Invoke(this, ex);
        }

        private void LogCategorizedError(string message, Exception ex, UpdateErrorCategory category)
        {
            string categoryPrefix = $"[{category}] ";

            App.LogService?.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                App.LogService?.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService?.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                switch (category)
                {
                    case UpdateErrorCategory.Network:
                        if (ex is System.Net.WebException webEx)
                        {
                            App.LogService?.LogDebug($"{categoryPrefix}Network status: {webEx.Status}");

                            if (webEx.Response != null)
                            {
                                var response = webEx.Response as System.Net.HttpWebResponse;
                                App.LogService?.LogDebug($"{categoryPrefix}HTTP Status: {response?.StatusCode} - {response?.StatusDescription}");
                            }
                        }
                        else if (ex is HttpRequestException httpEx)
                        {
                            App.LogService?.LogDebug($"{categoryPrefix}HTTP error: {httpEx.Message}");
                        }
                        break;

                    case UpdateErrorCategory.FileSystem:
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            App.LogService?.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                        }
                        break;

                    case UpdateErrorCategory.Extraction:
                        if (ex is InvalidDataException)
                        {
                            App.LogService?.LogDebug($"{categoryPrefix}The zip file appears to be corrupted");
                        }
                        break;
                }

                if (ex.InnerException != null)
                {
                    App.LogService?.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                    App.LogService?.LogDebug($"{categoryPrefix}Inner exception type: {ex.InnerException.GetType().Name}");
                }
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

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
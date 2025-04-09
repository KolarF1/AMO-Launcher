using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using AMO_Launcher.Utilities;
using AMO_Launcher.Services;

namespace AMO_Launcher.Services
{
    public class ManualGameIconService
    {
        private readonly string _iconStoragePath;

        public ManualGameIconService()
        {
            // Create a dedicated folder for manually added game icons using ErrorHandler
            _iconStoragePath = ErrorHandler.ExecuteSafe(() =>
            {
                // Create a dedicated folder for manually added game icons
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                string iconStoragePath = Path.Combine(appDataPath, "ManualIcons");

                // Ensure the directory exists
                if (!Directory.Exists(iconStoragePath))
                {
                    App.LogService.LogDebug($"Creating manual icons directory: {iconStoragePath}");
                    Directory.CreateDirectory(iconStoragePath);
                }

                App.LogService.Info($"Manual game icon storage path: {iconStoragePath}");
                return iconStoragePath;
            }, "Initializing ManualGameIconService", showErrorToUser: false, defaultValue: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher\\ManualIcons"));
        }

        // Get an icon for a manually added game
        public BitmapImage GetIconForGame(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Getting icon for game: {executablePath}");

                // First check if we have a saved icon
                string iconPath = GetIconFilePath(executablePath);
                if (File.Exists(iconPath))
                {
                    App.LogService.LogDebug($"Loading saved icon from {iconPath}");
                    return LoadIconFromFile(iconPath);
                }

                // If not, extract it from the executable and save it
                if (File.Exists(executablePath))
                {
                    // Track icon extraction performance
                    App.LogService.LogDebug($"[PERF] Starting: ExtractingIcon for {Path.GetFileName(executablePath)}");
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();

                    var icon = ExtractIconFromExecutable(executablePath);
                    if (icon != null)
                    {
                        SaveIconToFile(iconPath, icon);

                        stopwatch.Stop();
                        if (stopwatch.ElapsedMilliseconds > 1000)
                        {
                            App.LogService.Warning($"[PERF] Extracting icon for {Path.GetFileName(executablePath)} took {stopwatch.ElapsedMilliseconds}ms (exceeds 1000ms threshold)");
                        }
                        else
                        {
                            App.LogService.LogDebug($"[PERF] Extracting icon for {Path.GetFileName(executablePath)} took {stopwatch.ElapsedMilliseconds}ms");
                        }

                        App.LogService.Info($"Extracted and saved icon for {Path.GetFileName(executablePath)}");
                        return icon;
                    }

                    stopwatch.Stop();
                    App.LogService.LogDebug($"[PERF] Failed icon extraction took {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    LogCategorizedError($"Executable not found: {executablePath}", null, "FileSystem");
                }

                // Return null if all fails
                App.LogService.Warning($"Could not get icon for {executablePath}");
                return null;
            }, $"Getting icon for {Path.GetFileName(executablePath)}", showErrorToUser: false);
        }

        // Save an icon for a manually added game
        public void SaveIconForGame(string executablePath, BitmapImage icon)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService.Warning("SaveIconForGame called with null or empty executablePath");
                    return;
                }

                if (icon == null)
                {
                    App.LogService.Warning($"SaveIconForGame called with null icon for {executablePath}");
                    return;
                }

                // Save the icon to disk
                string iconPath = GetIconFilePath(executablePath);
                App.LogService.LogDebug($"Saving icon for {Path.GetFileName(executablePath)} to {iconPath}");
                SaveIconToFile(iconPath, icon);
                App.LogService.Info($"Saved icon for {Path.GetFileName(executablePath)}");
            }, $"Saving icon for {Path.GetFileName(executablePath)}");
        }

        // Get the file path for an icon
        private string GetIconFilePath(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // Use a hash of the path to create a unique filename
                string hash = GeneratePathHash(executablePath);
                string filePath = Path.Combine(_iconStoragePath, $"{hash}.png");
                App.LogService.Trace($"Icon file path for {Path.GetFileName(executablePath)}: {filePath}");
                return filePath;
            }, "Generating icon file path", showErrorToUser: false,
            defaultValue: Path.Combine(_iconStoragePath, "default.png"));
        }

        // Generate a hash for a path
        private string GeneratePathHash(string path)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(path);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);
                    string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    App.LogService.Trace($"Generated hash for {Path.GetFileName(path)}: {hash}");
                    return hash;
                }
            }, "Generating path hash", showErrorToUser: false,
            defaultValue: "default");
        }

        // Save an icon to a file
        private void SaveIconToFile(string filePath, BitmapImage icon)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Saving icon to file: {filePath}");

                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    App.LogService.LogDebug($"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    encoder.Save(stream);
                }

                App.LogService.LogDebug($"Icon saved successfully to {filePath}");
            }, $"Saving icon to {Path.GetFileName(filePath)}", showErrorToUser: false);
        }

        // Load an icon from a file
        private BitmapImage LoadIconFromFile(string filePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Loading icon from file: {filePath}");

                if (!File.Exists(filePath))
                {
                    LogCategorizedError($"Icon file not found: {filePath}", null, "FileSystem");
                    return null;
                }

                var icon = new BitmapImage();
                icon.BeginInit();
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.UriSource = new Uri(filePath, UriKind.Absolute);
                icon.EndInit();
                icon.Freeze(); // Make it thread-safe

                App.LogService.LogDebug($"Icon loaded successfully, dimensions: {icon.Width}x{icon.Height}");
                return icon;
            }, $"Loading icon from {Path.GetFileName(filePath)}", showErrorToUser: false);
        }

        // Extract icon from executable file
        private BitmapImage ExtractIconFromExecutable(string exePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Extracting icon from executable: {exePath}");

                if (string.IsNullOrEmpty(exePath))
                {
                    App.LogService.Warning("Null or empty executable path provided");
                    return null;
                }

                if (!File.Exists(exePath))
                {
                    LogCategorizedError($"Executable file not found: {exePath}", null, "FileSystem");
                    return null;
                }

                // Extract icon directly to byte array to avoid stream disposal issues
                byte[] iconData;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon == null)
                    {
                        App.LogService.Warning($"No icon found in executable: {exePath}");
                        return null;
                    }

                    App.LogService.Trace($"Extracted icon size: {icon.Width}x{icon.Height}");

                    using (var bitmap = icon.ToBitmap())
                    using (var stream = new MemoryStream())
                    {
                        // Save as PNG (better quality than BMP)
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        iconData = stream.ToArray();
                        App.LogService.Trace($"Converted icon to PNG, size: {iconData.Length} bytes");
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

                App.LogService.LogDebug($"Bitmap image created, dimensions: {bitmapImage.Width}x{bitmapImage.Height}");
                return bitmapImage;
            }, $"Extracting icon from {Path.GetFileName(exePath)}", showErrorToUser: false);
        }

        // Enhanced error logging with categorization
        private void LogCategorizedError(string message, Exception ex, string category)
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
                    case "FileSystem":
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            // Additional file system error details
                            App.LogService.LogDebug($"{categoryPrefix}File operation failed - check permissions and if file is in use");
                        }
                        break;

                    case "IconProcessing":
                        if (ex is ArgumentException || ex is FormatException)
                        {
                            App.LogService.LogDebug($"{categoryPrefix}Icon format error - possibly corrupted icon data");
                        }
                        break;
                }

                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    App.LogService.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }

}
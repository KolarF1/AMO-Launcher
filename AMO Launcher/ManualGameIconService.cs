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
            _iconStoragePath = ErrorHandler.ExecuteSafe(() =>
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AMO_Launcher");

                string iconStoragePath = Path.Combine(appDataPath, "ManualIcons");

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

        public BitmapImage GetIconForGame(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Getting icon for game: {executablePath}");

                string iconPath = GetIconFilePath(executablePath);
                if (File.Exists(iconPath))
                {
                    App.LogService.LogDebug($"Loading saved icon from {iconPath}");
                    return LoadIconFromFile(iconPath);
                }

                if (File.Exists(executablePath))
                {
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

                App.LogService.Warning($"Could not get icon for {executablePath}");
                return null;
            }, $"Getting icon for {Path.GetFileName(executablePath)}", showErrorToUser: false);
        }

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

                string iconPath = GetIconFilePath(executablePath);
                App.LogService.LogDebug($"Saving icon for {Path.GetFileName(executablePath)} to {iconPath}");
                SaveIconToFile(iconPath, icon);
                App.LogService.Info($"Saved icon for {Path.GetFileName(executablePath)}");
            }, $"Saving icon for {Path.GetFileName(executablePath)}");
        }

        private string GetIconFilePath(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                string hash = GeneratePathHash(executablePath);
                string filePath = Path.Combine(_iconStoragePath, $"{hash}.png");
                App.LogService.Trace($"Icon file path for {Path.GetFileName(executablePath)}: {filePath}");
                return filePath;
            }, "Generating icon file path", showErrorToUser: false,
            defaultValue: Path.Combine(_iconStoragePath, "default.png"));
        }

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

        private void SaveIconToFile(string filePath, BitmapImage icon)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Saving icon to file: {filePath}");

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
                icon.Freeze();

                App.LogService.LogDebug($"Icon loaded successfully, dimensions: {icon.Width}x{icon.Height}");
                return icon;
            }, $"Loading icon from {Path.GetFileName(filePath)}", showErrorToUser: false);
        }

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
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        iconData = stream.ToArray();
                        App.LogService.Trace($"Converted icon to PNG, size: {iconData.Length} bytes");
                    }
                }

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = new MemoryStream(iconData);
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                App.LogService.LogDebug($"Bitmap image created, dimensions: {bitmapImage.Width}x{bitmapImage.Height}");
                return bitmapImage;
            }, $"Extracting icon from {Path.GetFileName(exePath)}", showErrorToUser: false);
        }

        private void LogCategorizedError(string message, Exception ex, string category)
        {
            string categoryPrefix = $"[{category}] ";

            App.LogService.Error($"{categoryPrefix}{message}");

            if (ex != null)
            {
                App.LogService.LogDebug($"{categoryPrefix}Exception: {ex.GetType().Name}: {ex.Message}");
                App.LogService.LogDebug($"{categoryPrefix}Stack trace: {ex.StackTrace}");

                switch (category)
                {
                    case "FileSystem":
                        if (ex is IOException || ex is UnauthorizedAccessException)
                        {
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

                if (ex.InnerException != null)
                {
                    App.LogService.LogDebug($"{categoryPrefix}Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }

}
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Services
{
    public class IconCacheService
    {
        private static readonly Dictionary<string, BitmapImage> _iconCache = new Dictionary<string, BitmapImage>();

        public BitmapImage GetIcon(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Getting icon for executable: {executablePath}");

                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService.Warning("GetIcon called with null or empty path");
                    return null;
                }

                if (!File.Exists(executablePath))
                {
                    App.LogService.Warning($"Executable not found at path: {executablePath}");
                    return null;
                }

                lock (_iconCache)
                {
                    if (_iconCache.ContainsKey(executablePath))
                    {
                        App.LogService.LogDebug($"Icon found in cache for: {executablePath}");
                        return _iconCache[executablePath];
                    }
                }

                var stopwatch = Stopwatch.StartNew();
                BitmapImage icon = ExtractIconFromExecutable(executablePath);
                stopwatch.Stop();

                if (stopwatch.ElapsedMilliseconds > 500)
                {
                    App.LogService.Warning($"Icon extraction took {stopwatch.ElapsedMilliseconds}ms for {Path.GetFileName(executablePath)} (slow)");
                }
                else
                {
                    App.LogService.LogDebug($"Icon extraction completed in {stopwatch.ElapsedMilliseconds}ms for {Path.GetFileName(executablePath)}");
                }

                if (icon != null)
                {
                    lock (_iconCache)
                    {
                        App.LogService.LogDebug($"Adding icon to cache for: {executablePath}");
                        _iconCache[executablePath] = icon;
                    }
                }
                else
                {
                    App.LogService.Warning($"Failed to extract icon from: {executablePath}");
                }

                return icon;
            }, "Getting icon from cache or executable", true, null);
        }

        public void AddToCache(string executablePath, BitmapImage icon)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService.Warning("AddToCache called with null or empty path");
                    return;
                }

                if (icon == null)
                {
                    App.LogService.Warning($"AddToCache called with null icon for: {executablePath}");
                    return;
                }

                lock (_iconCache)
                {
                    App.LogService.LogDebug($"Adding icon to cache for: {executablePath}");
                    _iconCache[executablePath] = icon;
                }
            }, "Adding icon to cache", true);
        }

        public bool HasIcon(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(executablePath))
                {
                    App.LogService.LogDebug("HasIcon called with null or empty path");
                    return false;
                }

                lock (_iconCache)
                {
                    bool hasIcon = _iconCache.ContainsKey(executablePath);
                    App.LogService.LogDebug($"Icon cache {(hasIcon ? "contains" : "does not contain")} entry for: {executablePath}");
                    return hasIcon;
                }
            }, "Checking if icon is in cache", true, false);
        }

        public void ClearCache()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                lock (_iconCache)
                {
                    int count = _iconCache.Count;
                    App.LogService.Info($"Clearing icon cache, removing {count} entries");
                    _iconCache.Clear();
                }
            }, "Clearing icon cache", true);
        }

        private BitmapImage ExtractIconFromExecutable(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Extracting icon from executable: {executablePath}");

                BitmapImage defaultIcon = null;
                try
                {
                    App.LogService.LogDebug("Initializing default icon as fallback");
                    defaultIcon = new BitmapImage();
                    defaultIcon.BeginInit();
                    defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                    defaultIcon.EndInit();
                    defaultIcon.Freeze();
                    App.LogService.LogDebug("Default icon initialized successfully");
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Failed to load default icon: {ex.Message}");
                    App.LogService.LogDebug($"Default icon error details: {ex}");
                }

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService.Warning($"Invalid executable path for icon extraction: {executablePath}");
                    return defaultIcon;
                }

                App.LogService.Trace($"Starting icon extraction from: {executablePath}");

                byte[] iconData = null;

                try
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath))
                    {
                        if (icon == null)
                        {
                            App.LogService.Warning($"ExtractAssociatedIcon returned null for: {executablePath}");
                            return defaultIcon;
                        }

                        App.LogService.Trace($"Icon extracted, converting to bitmap");

                        using (var bitmap = icon.ToBitmap())
                        using (var stream = new MemoryStream())
                        {
                            App.LogService.Trace($"Saving icon as PNG");
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            iconData = stream.ToArray();
                            App.LogService.Trace($"Icon saved to memory stream, size: {iconData.Length} bytes");
                        }
                    }
                }
                catch (ArgumentException argEx)
                {
                    App.LogService.Error($"Invalid argument during icon extraction: {argEx.Message}");
                    App.LogService.LogDebug($"Argument exception details: {argEx}");
                    return defaultIcon;
                }
                catch (InvalidOperationException ioEx)
                {
                    App.LogService.Error($"Invalid operation during icon extraction: {ioEx.Message}");
                    App.LogService.LogDebug($"Invalid operation exception details: {ioEx}");
                    return defaultIcon;
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Icon extraction failed: {ex.Message}");
                    App.LogService.LogDebug($"Exception type: {ex.GetType().Name}");
                    App.LogService.LogDebug($"Stack trace: {ex.StackTrace}");
                    return defaultIcon;
                }

                if (iconData == null || iconData.Length == 0)
                {
                    App.LogService.Warning($"No icon data extracted from: {executablePath}");
                    return defaultIcon;
                }

                try
                {
                    App.LogService.Trace("Creating BitmapImage from extracted icon data");
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = new MemoryStream(iconData);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    App.LogService.LogDebug($"Successfully created BitmapImage, dimensions: {bitmapImage.Width}x{bitmapImage.Height}");
                    return bitmapImage;
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Failed to create BitmapImage from icon data: {ex.Message}");
                    App.LogService.LogDebug($"BitmapImage creation error details: {ex}");
                    return defaultIcon;
                }
            }, $"Extracting icon from {Path.GetFileName(executablePath)}", false, null);
        }

        public int GetCacheSize()
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                lock (_iconCache)
                {
                    return _iconCache.Count;
                }
            }, "Getting icon cache size", false, 0);
        }
    }
}
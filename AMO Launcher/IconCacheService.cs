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
        // Static cache for application lifetime
        private static readonly Dictionary<string, BitmapImage> _iconCache = new Dictionary<string, BitmapImage>();

        /// <summary>
        /// Get an icon from the cache or extract from the executable
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>BitmapImage of the icon or null if extraction fails</returns>
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

                // Check if the icon is already in the cache
                lock (_iconCache)
                {
                    if (_iconCache.ContainsKey(executablePath))
                    {
                        App.LogService.LogDebug($"Icon found in cache for: {executablePath}");
                        return _iconCache[executablePath];
                    }
                }

                // Extract the icon with performance tracking
                var stopwatch = Stopwatch.StartNew();
                BitmapImage icon = ExtractIconFromExecutable(executablePath);
                stopwatch.Stop();

                // Log performance metrics for extraction
                if (stopwatch.ElapsedMilliseconds > 500)
                {
                    App.LogService.Warning($"Icon extraction took {stopwatch.ElapsedMilliseconds}ms for {Path.GetFileName(executablePath)} (slow)");
                }
                else
                {
                    App.LogService.LogDebug($"Icon extraction completed in {stopwatch.ElapsedMilliseconds}ms for {Path.GetFileName(executablePath)}");
                }

                // Cache it if successfully extracted
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

        /// <summary>
        /// Add an icon to the cache
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <param name="icon">Icon to cache</param>
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

        /// <summary>
        /// Check if an icon is in the cache
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>True if the icon is cached</returns>
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

        /// <summary>
        /// Clear the icon cache
        /// </summary>
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

        /// <summary>
        /// Extract icon from executable file
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>BitmapImage of the icon or null if extraction fails</returns>
        private BitmapImage ExtractIconFromExecutable(string executablePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.LogDebug($"Extracting icon from executable: {executablePath}");

                // Create a default icon in case extraction fails
                BitmapImage defaultIcon = null;
                try
                {
                    App.LogService.LogDebug("Initializing default icon as fallback");
                    defaultIcon = new BitmapImage();
                    defaultIcon.BeginInit();
                    defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                    defaultIcon.EndInit();
                    defaultIcon.Freeze(); // Make it thread-safe
                    App.LogService.LogDebug("Default icon initialized successfully");
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Failed to load default icon: {ex.Message}");
                    App.LogService.LogDebug($"Default icon error details: {ex}");
                    // Continue without default icon
                }

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.LogService.Warning($"Invalid executable path for icon extraction: {executablePath}");
                    return defaultIcon;
                }

                // Track individual extraction steps
                App.LogService.Trace($"Starting icon extraction from: {executablePath}");

                // Extract icon directly to byte array to avoid stream disposal issues
                byte[] iconData = null;

                // Extract the icon with proper error handling for specific issues
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
                            // Save as PNG (better quality than BMP)
                            App.LogService.Trace($"Saving icon as PNG");
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            iconData = stream.ToArray();
                            App.LogService.Trace($"Icon saved to memory stream, size: {iconData.Length} bytes");
                        }
                    }
                }
                catch (ArgumentException argEx)
                {
                    // Common issue with some executables
                    App.LogService.Error($"Invalid argument during icon extraction: {argEx.Message}");
                    App.LogService.LogDebug($"Argument exception details: {argEx}");
                    return defaultIcon;
                }
                catch (InvalidOperationException ioEx)
                {
                    // Can occur with corrupted or unusual executables
                    App.LogService.Error($"Invalid operation during icon extraction: {ioEx.Message}");
                    App.LogService.LogDebug($"Invalid operation exception details: {ioEx}");
                    return defaultIcon;
                }
                catch (Exception ex)
                {
                    // General extraction failure
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

                // Create bitmap image from byte array
                try
                {
                    App.LogService.Trace("Creating BitmapImage from extracted icon data");
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = new MemoryStream(iconData);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it thread-safe

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

        /// <summary>
        /// Get the number of icons in the cache
        /// </summary>
        /// <returns>Number of cached icons</returns>
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
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace AMO_Launcher.Services
{
    /// <summary>
    /// Provides application-wide caching for game icons
    /// </summary>
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
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                return null;

            // Check if the icon is already in the cache
            lock (_iconCache)
            {
                if (_iconCache.ContainsKey(executablePath))
                {
                    return _iconCache[executablePath];
                }
            }

            // Extract the icon
            BitmapImage icon = ExtractIconFromExecutable(executablePath);

            // Cache it if successfully extracted
            if (icon != null)
            {
                lock (_iconCache)
                {
                    _iconCache[executablePath] = icon;
                }
            }

            return icon;
        }

        /// <summary>
        /// Add an icon to the cache
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <param name="icon">Icon to cache</param>
        public void AddToCache(string executablePath, BitmapImage icon)
        {
            if (!string.IsNullOrEmpty(executablePath) && icon != null)
            {
                lock (_iconCache)
                {
                    _iconCache[executablePath] = icon;
                }
            }
        }

        /// <summary>
        /// Check if an icon is in the cache
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>True if the icon is cached</returns>
        public bool HasIcon(string executablePath)
        {
            lock (_iconCache)
            {
                return !string.IsNullOrEmpty(executablePath) && _iconCache.ContainsKey(executablePath);
            }
        }

        /// <summary>
        /// Clear the icon cache
        /// </summary>
        public void ClearCache()
        {
            lock (_iconCache)
            {
                _iconCache.Clear();
            }
        }

        /// <summary>
        /// Extract icon from executable file
        /// </summary>
        /// <param name="executablePath">Path to the executable</param>
        /// <returns>BitmapImage of the icon or null if extraction fails</returns>
        private BitmapImage ExtractIconFromExecutable(string executablePath)
        {
            try
            {
                // Create a default icon in case extraction fails
                BitmapImage defaultIcon = null;
                try
                {
                    defaultIcon = new BitmapImage();
                    defaultIcon.BeginInit();
                    defaultIcon.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultGameIcon.png", UriKind.Absolute);
                    defaultIcon.EndInit();
                    defaultIcon.Freeze(); // Make it thread-safe
                }
                catch
                {
                    // If default icon loading fails, continue without it
                }

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                    return defaultIcon;

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
                System.Diagnostics.Debug.WriteLine($"Error extracting icon from {executablePath}: {ex.Message}");
                return null;
            }
        }
    }
}
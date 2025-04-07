using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;

namespace AMO_Launcher.Services
{
    public class ManualGameIconService
    {
        private readonly string _iconStoragePath;

        public ManualGameIconService()
        {
            // Create a dedicated folder for manually added game icons
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            _iconStoragePath = Path.Combine(appDataPath, "ManualIcons");

            // Ensure the directory exists
            if (!Directory.Exists(_iconStoragePath))
            {
                Directory.CreateDirectory(_iconStoragePath);
            }

            Console.WriteLine($"Manual game icon storage path: {_iconStoragePath}");
        }

        // Get an icon for a manually added game
        public BitmapImage GetIconForGame(string executablePath)
        {
            try
            {
                // First check if we have a saved icon
                string iconPath = GetIconFilePath(executablePath);
                if (File.Exists(iconPath))
                {
                    Console.WriteLine($"Loading saved icon for {executablePath} from {iconPath}");
                    return LoadIconFromFile(iconPath);
                }

                // If not, extract it from the executable and save it
                if (File.Exists(executablePath))
                {
                    var icon = ExtractIconFromExecutable(executablePath);
                    if (icon != null)
                    {
                        SaveIconToFile(iconPath, icon);
                        Console.WriteLine($"Extracted and saved icon for {executablePath} to {iconPath}");
                        return icon;
                    }
                }

                // Return null if all fails
                Console.WriteLine($"Could not get icon for {executablePath}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting icon for {executablePath}: {ex.Message}");
                return null;
            }
        }

        // Save an icon for a manually added game
        public void SaveIconForGame(string executablePath, BitmapImage icon)
        {
            if (string.IsNullOrEmpty(executablePath) || icon == null)
                return;

            try
            {
                // Save the icon to disk
                string iconPath = GetIconFilePath(executablePath);
                SaveIconToFile(iconPath, icon);
                Console.WriteLine($"Saved icon for {executablePath} to {iconPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving icon for {executablePath}: {ex.Message}");
            }
        }

        // Get the file path for an icon
        private string GetIconFilePath(string executablePath)
        {
            // Use a hash of the path to create a unique filename
            string hash = GeneratePathHash(executablePath);
            return Path.Combine(_iconStoragePath, $"{hash}.png");
        }

        // Generate a hash for a path
        private string GeneratePathHash(string path)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(path);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        // Save an icon to a file
        private void SaveIconToFile(string filePath, BitmapImage icon)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    encoder.Save(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving icon to file {filePath}: {ex.Message}");
            }
        }

        // Load an icon from a file
        private BitmapImage LoadIconFromFile(string filePath)
        {
            try
            {
                var icon = new BitmapImage();
                icon.BeginInit();
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.UriSource = new Uri(filePath, UriKind.Absolute);
                icon.EndInit();
                icon.Freeze(); // Make it thread-safe
                return icon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading icon from file {filePath}: {ex.Message}");
                return null;
            }
        }

        // Extract icon from executable file
        private BitmapImage ExtractIconFromExecutable(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    return null;

                // Extract icon directly to byte array to avoid stream disposal issues
                byte[] iconData;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon == null)
                        return null;

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
                Console.WriteLine($"Error extracting icon from {exePath}: {ex.Message}");
                return null;
            }
        }
    }
}
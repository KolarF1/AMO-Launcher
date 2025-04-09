using System;
using System.IO;

namespace AMO_Launcher.Utilities
{
    public static class PathUtility
    {
        // Convert absolute path to relative path based on app directory
        public static string ToRelativePath(string absolutePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Starting path conversion to relative: {absolutePath}");

                if (string.IsNullOrEmpty(absolutePath))
                {
                    App.LogService?.LogDebug("Path is null or empty, returning as is");
                    return absolutePath;
                }

                // Get the application base directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                App.LogService?.Trace($"Using base directory: {baseDir}");

                // Check if the path is already relative
                if (!Path.IsPathRooted(absolutePath))
                {
                    App.LogService?.LogDebug("Path is already relative, no conversion needed");
                    return absolutePath;
                }

                // Check if the path is within the app directory
                if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = absolutePath.Substring(baseDir.Length);
                    App.LogService?.LogDebug($"Successfully converted to relative path: {relativePath}");
                    return relativePath;
                }

                // If not within app directory, return as is
                App.LogService?.LogDebug("Path is outside app directory, cannot convert to relative");
                return absolutePath;
            }, "Converting absolute to relative path", true, absolutePath);
        }

        // Convert relative path to absolute path
        public static string ToAbsolutePath(string relativePath)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService?.LogDebug($"Starting path conversion to absolute: {relativePath}");

                if (string.IsNullOrEmpty(relativePath))
                {
                    App.LogService?.LogDebug("Path is null or empty, returning as is");
                    return relativePath;
                }

                // If already absolute, return as is
                if (Path.IsPathRooted(relativePath))
                {
                    App.LogService?.LogDebug("Path is already absolute, no conversion needed");
                    return relativePath;
                }

                // Combine with app base directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                App.LogService?.Trace($"Using base directory: {baseDir}");

                string absolutePath = Path.Combine(baseDir, relativePath);
                App.LogService?.LogDebug($"Successfully converted to absolute path: {absolutePath}");
                return absolutePath;
            }, "Converting relative to absolute path", true, relativePath);
        }
    }
}
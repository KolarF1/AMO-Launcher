using System;
using System.IO;

namespace AMO_Launcher.Utilities
{
    public static class PathUtility
    {
        // Convert absolute path to relative path based on app directory
        public static string ToRelativePath(string absolutePath)
        {
            try
            {
                if (string.IsNullOrEmpty(absolutePath))
                    return absolutePath;

                // Get the application base directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Check if the path is already relative
                if (!Path.IsPathRooted(absolutePath))
                    return absolutePath;

                // Check if the path is within the app directory
                if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return absolutePath.Substring(baseDir.Length);
                }

                // If not within app directory, return as is
                return absolutePath;
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error converting to relative path: {ex.Message}");
                return absolutePath;
            }
        }

        // Convert relative path to absolute path
        public static string ToAbsolutePath(string relativePath)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath))
                    return relativePath;

                // If already absolute, return as is
                if (Path.IsPathRooted(relativePath))
                    return relativePath;

                // Combine with app base directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, relativePath);
            }
            catch (Exception ex)
            {
                App.LogToFile($"Error converting to absolute path: {ex.Message}");
                return relativePath;
            }
        }
    }
}
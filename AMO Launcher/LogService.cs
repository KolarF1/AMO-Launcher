using System;
using System.Diagnostics; // System.Diagnostics.Debug
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AMO_Launcher.Services
{
    public enum LogLevel
    {
        ERROR = 0,
        WARNING = 1,
        INFO = 2,
        DEBUG = 3,
        TRACE = 4
    }

    public class LogService
    {
        // Log file path
        private string _logFilePath;

        // Configuration
        private bool _detailedLoggingEnabled = false;
        private ConfigurationService _configService;

        // Log level thresholds
        private LogLevel _standardLogLevel = LogLevel.INFO;  // Standard logging includes ERROR, WARNING, INFO
        private LogLevel _detailedLogLevel = LogLevel.TRACE; // Detailed logging includes all levels

        // Default constructor
        public LogService()
        {
            // Initialize log file path
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            // Create directory if it doesn't exist
            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch
                {
                    // If we can't create the directory, fall back to desktop
                    _logFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "AMO_Launcher_Debug.log");
                    return;
                }
            }

            // Set the log file path
            _logFilePath = Path.Combine(appDataPath, "AMO_Launcher_Debug.log");
        }

        // Initialize with configuration service
        public void Initialize(ConfigurationService configService)
        {
            _configService = configService;
            _detailedLoggingEnabled = configService?.GetEnableDetailedLogging() ?? false;

            // Log initialization success
            Log(LogLevel.INFO, "LogService initialized");
        }

        // Update detailed logging flag (for when settings change)
        public void UpdateDetailedLogging(bool enableDetailedLogging)
        {
            _detailedLoggingEnabled = enableDetailedLogging;
            Log(LogLevel.INFO, $"Detailed logging {(_detailedLoggingEnabled ? "enabled" : "disabled")}");
        }

        // Primary logging method
        public void Log(
            LogLevel level,
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            try
            {
                // Check if the message should be logged based on the level
                if (!ShouldLogMessage(level))
                    return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logMessage;

                // Format based on whether detailed logging is enabled
                if (_detailedLoggingEnabled && level > LogLevel.INFO)
                {
                    // Detailed format with caller information
                    string fileName = Path.GetFileName(sourceFilePath);
                    string className = GetClassNameFromFilePath(sourceFilePath);
                    logMessage = $"[{timestamp}] [{level}] [{className}.{memberName}::{sourceLineNumber}] {message}";
                }
                else
                {
                    // Standard format
                    logMessage = $"[{timestamp}] [{level}] {message}";
                }

                // Write to log file
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);

                // Also output to debug console for development
                System.Diagnostics.Debug.WriteLine(logMessage);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        // Helper method to determine if a message should be logged
        private bool ShouldLogMessage(LogLevel level)
        {
            if (_detailedLoggingEnabled)
            {
                return level <= _detailedLogLevel;
            }
            else
            {
                return level <= _standardLogLevel;
            }
        }

        // Helper to extract class name from file path
        private string GetClassNameFromFilePath(string filePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                return fileName;
            }
            catch
            {
                return "UnknownClass";
            }
        }

        // Convenience methods for different log levels
        public void Error(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(LogLevel.ERROR, message, memberName, sourceFilePath, sourceLineNumber);
        }

        public void Warning(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(LogLevel.WARNING, message, memberName, sourceFilePath, sourceLineNumber);
        }

        public void Info(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(LogLevel.INFO, message, memberName, sourceFilePath, sourceLineNumber);
        }

        // Renamed from Debug to LogDebug to avoid naming conflicts with System.Diagnostics.Debug
        public void LogDebug(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(LogLevel.DEBUG, message, memberName, sourceFilePath, sourceLineNumber);
        }

        public void Trace(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(LogLevel.TRACE, message, memberName, sourceFilePath, sourceLineNumber);
        }

        // Clear the log file
        public void ClearLogFile()
        {
            try
            {
                File.WriteAllText(_logFilePath, string.Empty);
                Info("Log file cleared");
            }
            catch (Exception ex)
            {
                Error($"Failed to clear log file: {ex.Message}");
            }
        }

        // Get the log file path
        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        // Create a backup of the log file
        public string CreateLogBackup()
        {
            try
            {
                string backupPath = _logFilePath + $".{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                File.Copy(_logFilePath, backupPath, true);
                Info($"Created log backup at {backupPath}");
                return backupPath;
            }
            catch (Exception ex)
            {
                Error($"Failed to create log backup: {ex.Message}");
                return null;
            }
        }
    }

    public static class LogServiceExtensions
    {
        // Extension method for ShouldLogTrace capability
        public static bool ShouldLogTrace(this LogService logService)
        {
            // Always return true - the LogService.Trace method will handle filtering
            // This is just to keep existing code functioning
            return true;
        }

        // Extension method for ShouldLogDebug capability
        public static bool ShouldLogDebug(this LogService logService)
        {
            // Always return true - the LogService.LogDebug method will handle filtering
            // This is just to keep existing code functioning
            return true;
        }
    }
}
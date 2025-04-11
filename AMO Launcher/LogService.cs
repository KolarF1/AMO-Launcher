using System;
using System.Diagnostics;
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
        private string _logFilePath;
        private bool _detailedLoggingEnabled = false;
        private ConfigurationService _configService;
        private LogLevel _standardLogLevel = LogLevel.INFO;
        private LogLevel _detailedLogLevel = LogLevel.TRACE;

        public LogService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AMO_Launcher");

            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch
                {
                    _logFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "AMO_Launcher_Debug.log");
                    return;
                }
            }

            _logFilePath = Path.Combine(appDataPath, "AMO_Launcher_Debug.log");
        }

        public void Initialize(ConfigurationService configService)
        {
            _configService = configService;
            _detailedLoggingEnabled = configService?.GetEnableDetailedLogging() ?? false;

            Log(LogLevel.INFO, "LogService initialized");
        }

        public void UpdateDetailedLogging(bool enableDetailedLogging)
        {
            _detailedLoggingEnabled = enableDetailedLogging;
            Log(LogLevel.INFO, $"Detailed logging {(_detailedLoggingEnabled ? "enabled" : "disabled")}");
        }

        public void Log(
            LogLevel level,
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            try
            {
                if (!ShouldLogMessage(level))
                    return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logMessage;

                if (_detailedLoggingEnabled && level > LogLevel.INFO)
                {
                    string fileName = Path.GetFileName(sourceFilePath);
                    string className = GetClassNameFromFilePath(sourceFilePath);
                    logMessage = $"[{timestamp}] [{level}] [{className}.{memberName}::{sourceLineNumber}] {message}";
                }
                else
                {
                    logMessage = $"[{timestamp}] [{level}] {message}";
                }

                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(logMessage);
            }
            catch
            {
            }
        }

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

        public string GetLogFilePath()
        {
            return _logFilePath;
        }

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
        public static bool ShouldLogTrace(this LogService logService)
        {
            return true;
        }

        public static bool ShouldLogDebug(this LogService logService)
        {
            return true;
        }
    }
}
using System;
using System.Threading.Tasks;
using System.Windows;

namespace AMO_Launcher.Utilities
{
    public static class ErrorHandler
    {
        // Execute an action with proper error handling
        public static void ExecuteSafe(Action action, string operationName, bool showErrorToUser = true)
        {
            try
            {
                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Starting operation: {operationName}");
                }

                action();

                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Completed operation: {operationName}");
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, operationName, showErrorToUser);
            }
        }

        // Execute a function with proper error handling and return a result
        public static T ExecuteSafe<T>(Func<T> func, string operationName, bool showErrorToUser = true, T defaultValue = default)
        {
            try
            {
                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Starting operation: {operationName}");
                }

                T result = func();

                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Completed operation: {operationName}");
                }

                return result;
            }
            catch (Exception ex)
            {
                HandleException(ex, operationName, showErrorToUser);
                return defaultValue;
            }
        }

        // Execute an async action with proper error handling
        public static async Task ExecuteSafeAsync(Func<Task> asyncAction, string operationName, bool showErrorToUser = true)
        {
            try
            {
                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Starting async operation: {operationName}");
                }

                await asyncAction();

                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Completed async operation: {operationName}");
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, operationName, showErrorToUser);
            }
        }

        // Execute an async function with proper error handling and return a result
        public static async Task<T> ExecuteSafeAsync<T>(Func<Task<T>> asyncFunc, string operationName, bool showErrorToUser = true, T defaultValue = default)
        {
            try
            {
                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Starting async operation: {operationName}");
                }

                T result = await asyncFunc();

                if (App.LogService != null)
                {
                    App.LogService.LogDebug($"Completed async operation: {operationName}");
                }

                return result;
            }
            catch (Exception ex)
            {
                HandleException(ex, operationName, showErrorToUser);
                return defaultValue;
            }
        }

        // Common exception handling logic
        private static void HandleException(Exception ex, string operationName, bool showErrorToUser)
        {
            // Log the error with context
            if (App.LogService != null)
            {
                App.LogService.Error($"Error in {operationName}: {ex.Message}");

                // Log detailed exception information when detailed logging is enabled
                App.LogService.LogDebug($"Exception details: {ex.GetType().Name}");
                App.LogService.LogDebug($"Stack trace: {ex.StackTrace}");

                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    App.LogService.LogDebug($"Inner exception: {ex.InnerException.Message}");
                    App.LogService.LogDebug($"Inner stack trace: {ex.InnerException.StackTrace}");
                }
            }
            else
            {
                // Fallback logging if LogService is not available
                App.LogError($"Error in {operationName}: {ex.Message}", ex);
            }

            // Show error to user if requested
            if (showErrorToUser)
            {
                string errorMessage = $"An error occurred during {operationName}: {ex.Message}";

                // Show a message box on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        errorMessage,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }
    }
}
using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using AMO_Launcher.Utilities;

namespace AMO_Launcher
{
    public class ReverseOrderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                // Trace level logging for detailed flow tracking
                App.LogService?.Trace($"Converting value: {value}, targetType: {targetType?.Name}");

                if (value is int index)
                {
                    // Find the parent ListView
                    var listView = ItemsControl.ItemsControlFromItemContainer(
                        (System.Windows.DependencyObject)parameter) as ListView;

                    if (listView != null)
                    {
                        // Calculate reversed index (1-based)
                        int result = listView.Items.Count - index;
                        App.LogService?.LogDebug($"Calculated reverse index: {result} from list count: {listView.Items.Count} and index: {index}");
                        return result;
                    }
                    else
                    {
                        App.LogService?.Warning("Could not find parent ListView for converter");
                    }
                }
                else
                {
                    // Warning for potential data binding issues
                    App.LogService?.Warning($"Expected int value for ReverseOrderConverter, got: {value?.GetType().Name ?? "null"}");
                }

                App.LogService?.LogDebug("Returning default value (1) from converter");
                return 1; // Default
            }, "ReverseOrderConverter.Convert", defaultValue: 1);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Log the method call before throwing exception
            App.LogService?.LogDebug($"ConvertBack called with value: {value}, targetType: {targetType?.Name}");

            // This exception is expected behavior for one-way converters
            throw new NotImplementedException("ReverseOrderConverter.ConvertBack is not implemented");
        }
    }
}
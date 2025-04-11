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
                App.LogService?.Trace($"Converting value: {value}, targetType: {targetType?.Name}");

                if (value is int index)
                {
                    var listView = ItemsControl.ItemsControlFromItemContainer(
                        (System.Windows.DependencyObject)parameter) as ListView;

                    if (listView != null)
                    {
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
                    App.LogService?.Warning($"Expected int value for ReverseOrderConverter, got: {value?.GetType().Name ?? "null"}");
                }

                App.LogService?.LogDebug("Returning default value (1) from converter");
                return 1;
            }, "ReverseOrderConverter.Convert", defaultValue: 1);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            App.LogService?.LogDebug($"ConvertBack called with value: {value}, targetType: {targetType?.Name}");

            throw new NotImplementedException("ReverseOrderConverter.ConvertBack is not implemented");
        }
    }
}
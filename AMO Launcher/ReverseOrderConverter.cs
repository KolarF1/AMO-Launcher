using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace AMO_Launcher
{
    public class ReverseOrderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                // Find the parent ListView
                var listView = ItemsControl.ItemsControlFromItemContainer(
                    (System.Windows.DependencyObject)parameter) as ListView;

                if (listView != null)
                {
                    // Calculate reversed index (1-based)
                    return listView.Items.Count - index;
                }
            }
            return 1; // Default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
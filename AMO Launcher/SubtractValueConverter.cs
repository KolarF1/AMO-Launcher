using System;
using System.Globalization;
using System.Windows.Data;

namespace AMO_Launcher.Converters
{
    public class SubtractValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double totalWidth && parameter != null && double.TryParse(parameter.ToString(), out double subtractValue))
            {
                return Math.Max(0, totalWidth - subtractValue);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

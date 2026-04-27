using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TWChatOverlay.Converters
{
    public class StringEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return Visibility.Collapsed;
            string param = parameter.ToString() ?? string.Empty;
            string val = value?.ToString() ?? string.Empty;
            return string.Equals(val, param, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

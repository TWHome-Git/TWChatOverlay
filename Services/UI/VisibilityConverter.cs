using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TWChatOverlay.Services
{
    public class VisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;

            if (value is Visibility v) return v;

            if (value is bool b) return b ? Visibility.Visible : Visibility.Collapsed;

            if (value is int i) return i != 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is long l) return l != 0L ? Visibility.Visible : Visibility.Collapsed;

            var s = value.ToString();
            if (string.IsNullOrEmpty(s)) return Visibility.Collapsed;

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility vis)
            {
                if (targetType == typeof(bool) || targetType == typeof(bool?))
                    return vis == Visibility.Visible;

                return vis == Visibility.Visible ? 1 : 0;
            }

            return Binding.DoNothing;
        }
    }
}

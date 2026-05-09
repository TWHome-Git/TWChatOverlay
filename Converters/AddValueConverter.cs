using System;
using System.Globalization;
using System.Windows.Data;

namespace TWChatOverlay.Converters
{
    public class AddValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out double baseValue))
                return value;
            if (!double.TryParse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out double add))
                add = 0d;
            return baseValue + add;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}


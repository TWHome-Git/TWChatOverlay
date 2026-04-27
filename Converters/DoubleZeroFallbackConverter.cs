using System;
using System.Globalization;
using System.Windows.Data;

namespace TWChatOverlay.Converters
{
    /// <summary>
    /// 빈 문자열이나 잘못된 입력을 0으로 변환하는 Converter
    /// </summary>
    public class DoubleZeroFallbackConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return d.ToString("F0", culture);

            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && double.TryParse(s, NumberStyles.Any, culture, out double result))
                return result;

            return 0.0;
        }
    }
}

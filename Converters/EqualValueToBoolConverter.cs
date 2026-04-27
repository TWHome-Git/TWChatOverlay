using System;
using System.Globalization;
using System.Windows.Data;

namespace TWChatOverlay.Converters
{
    /// <summary>
    /// 값과 파라미터를 비교하여 같으면 true를 반환하는 Converter
    /// </summary>
    public class EqualValueToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            return value.Equals(int.TryParse(parameter.ToString(), out int paramInt) ? paramInt : parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool?)value == true && parameter != null)
            {
                return int.TryParse(parameter.ToString(), out int paramInt) ? paramInt : parameter;
            }
            return Binding.DoNothing;
        }
    }
}

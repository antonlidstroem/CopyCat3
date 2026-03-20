using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CopyCat.Converters
{
    public class BoolToStringConverter : IValueConverter
    {
        public string TrueValue { get; set; } = string.Empty;
        public string FalseValue { get; set; } = string.Empty;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? TrueValue : FalseValue;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

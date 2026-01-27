using System;
using System.Globalization;
using System.Windows.Data;

namespace MusicApp.Converters
{
    /// <summary>
    /// Converts slider value, maximum, and width to a percentage-based width for visual feedback
    /// </summary>
    public class SliderValueToWidthMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length >= 3 && values[0] is double value && values[1] is double maximum && values[2] is double width)
            {
                if (maximum > 0)
                {
                    return (value / maximum) * width;
                }
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a single slider value to a percentage-based width
    /// </summary>
    public class SliderValueToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double sliderValue && parameter is double maxWidth)
            {
                return (sliderValue / 100.0) * maxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts DateTime to formatted date string
    /// </summary>
    public class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                if (dateTime == DateTime.MinValue)
                    return "";
                
                // Use short date format
                return dateTime.ToString("M/d/yyyy", culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts nullable DateTime to formatted date string
    /// </summary>
    public class NullableDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("M/d/yyyy", culture);
            }
            
            var nullableDate = value as DateTime?;
            if (nullableDate.HasValue)
            {
                return nullableDate.Value.ToString("M/d/yyyy", culture);
            }
            
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts file size (long) to human-readable string (KB, MB, GB)
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long fileSize)
            {
                if (fileSize == 0)
                    return "";

                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = fileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts integer to string, showing empty string for zero
    /// </summary>
    public class IntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0 ? "" : intValue.ToString(culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 
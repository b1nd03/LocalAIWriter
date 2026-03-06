using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LocalAIWriter.Core.Extensions;

namespace LocalAIWriter.Converters;

/// <summary>Converts bool to Visibility (true=Visible, false=Collapsed).</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool isVisible = value is true;
        if (invert) isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

/// <summary>Converts DiffType to a color for inline diff rendering.</summary>
[ValueConversion(typeof(DiffType), typeof(Brush))]
public sealed class DiffTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DiffType diffType)
        {
            return diffType switch
            {
                DiffType.Added => new SolidColorBrush(Color.FromArgb(64, 0, 200, 83)),
                DiffType.Removed => new SolidColorBrush(Color.FromArgb(64, 255, 82, 82)),
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts confidence score (0.0-1.0) to a color gradient.</summary>
[ValueConversion(typeof(float), typeof(Brush))]
public sealed class ConfidenceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float confidence)
        {
            return confidence switch
            {
                >= 0.9f => new SolidColorBrush(Color.FromRgb(0, 200, 83)),    // Green
                >= 0.7f => new SolidColorBrush(Color.FromRgb(255, 183, 77)),   // Amber
                >= 0.5f => new SolidColorBrush(Color.FromRgb(255, 152, 0)),    // Orange
                _ => new SolidColorBrush(Color.FromRgb(255, 82, 82))           // Red
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

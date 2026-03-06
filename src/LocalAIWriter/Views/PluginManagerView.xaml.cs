using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LocalAIWriter.Views;

public partial class PluginManagerView : UserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }
}

/// <summary>Shows element only when count is zero (for empty states).</summary>
public class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

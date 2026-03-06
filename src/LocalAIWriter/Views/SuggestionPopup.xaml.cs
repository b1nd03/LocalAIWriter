using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace LocalAIWriter.Views;

public partial class SuggestionPopup : Window
{
    public SuggestionPopup()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var vm = DataContext as ViewModels.SuggestionPopupViewModel;
            vm?.DismissCommand.Execute(null);
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            var vm = DataContext as ViewModels.SuggestionPopupViewModel;
            vm?.AcceptCommand.Execute(null);
        }
    }
}

/// <summary>Converts float (0-1) to percentage (0-100) for progress bars.</summary>
public class FloatToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is float f ? (double)(f * 100) : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

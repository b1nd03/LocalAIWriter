using System.Windows;

namespace LocalAIWriter.Views;

public partial class PrivacyInfoWindow : Window
{
    public PrivacyInfoWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

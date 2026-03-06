using System.Windows;
using System.Windows.Controls;
using LocalAIWriter.ViewModels;

namespace LocalAIWriter.Views;

public partial class SettingsWindow : Window
{
    private StackPanel[]? _panels;

    public SettingsWindow()
    {
        InitializeComponent();

        // Initialize AFTER InitializeComponent so named elements are ready
        _panels = new[]
        {
            PanelGeneral,
            PanelHotkey,
            PanelAppearance,
            PanelAccessibility,
            PanelStatus
        };
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: fires during InitializeComponent before _panels is set
        if (_panels == null) return;

        int index = NavList.SelectedIndex;
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            try
            {
                vm.SaveCommand.Execute(null);
                MessageBox.Show("Settings saved.", "LocalAI Writer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings:\n{ex.Message}", "LocalAI Writer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        Close();
    }
}

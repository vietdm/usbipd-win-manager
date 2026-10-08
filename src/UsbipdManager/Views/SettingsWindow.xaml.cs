using System.Windows;
using UsbipdManager.ViewModels;

namespace UsbipdManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

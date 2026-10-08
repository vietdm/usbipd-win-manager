using System.Windows;
using UsbipdManager.ViewModels;

namespace UsbipdManager.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(HelpViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => CloseButton.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

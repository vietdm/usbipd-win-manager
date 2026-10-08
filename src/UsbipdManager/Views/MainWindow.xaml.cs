using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsbipdManager.ViewModels;

namespace UsbipdManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ScrollViewer? _consoleScroll;
    private bool _followNewest = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        ConsoleList.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopySelection, (_, e) => e.CanExecute = ConsoleList.SelectedItems.Count > 0));
        ConsoleList.Loaded += OnConsoleLoaded;
    }

    /// <summary>Raised when the close button hid the window instead of closing it.</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>Only the exit sequence sets this; otherwise closing hides the window to the tray.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }

        base.OnClosing(e);
    }

    private void OnConsoleLoaded(object sender, RoutedEventArgs e)
    {
        if (_consoleScroll is not null)
        {
            return;
        }

        _consoleScroll = FindDescendant<ScrollViewer>(ConsoleList);
        if (_consoleScroll is null)
        {
            return;
        }

        _consoleScroll.ScrollChanged += OnConsoleScrollChanged;
        _consoleScroll.ScrollToEnd();
    }

    // Follow the newest line unless the user scrolled up; content growth alone never turns following off.
    private void OnConsoleScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_consoleScroll is null)
        {
            return;
        }

        if (e.ExtentHeightChange == 0)
        {
            _followNewest = _consoleScroll.VerticalOffset >= _consoleScroll.ScrollableHeight - 0.5;
        }
        else if (_followNewest)
        {
            _consoleScroll.ScrollToEnd();
        }

        JumpToLatestButton.Visibility = _followNewest || _viewModel.Console.Lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnJumpToLatest(object sender, RoutedEventArgs e)
    {
        _followNewest = true;
        _consoleScroll?.ScrollToEnd();
        JumpToLatestButton.Visibility = Visibility.Collapsed;
    }

    private void OnCopySelection(object sender, ExecutedRoutedEventArgs e)
    {
        var selected = ConsoleList.SelectedItems.Cast<LogLine>()
            .OrderBy(line => ConsoleList.Items.IndexOf(line));
        ConsoleViewModel.TrySetClipboard(_viewModel.Console.GetText(selected));
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

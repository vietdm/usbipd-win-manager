using System.Windows;
using System.Windows.Input;
using UsbipdManager.Services;

namespace UsbipdManager.Views;

/// <summary>Styled replacement for MessageBox: info / warning / error messages and confirmations.</summary>
public partial class MessageDialog : Window
{
    private bool _result;

    public MessageDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PrimaryButton.Focus();
    }

    /// <summary>Shows the dialog modally; returns true when the primary button was chosen.</summary>
    public static bool Show(Window? owner, string title, string message, DialogKind kind, string primaryText, string? secondaryText = null, bool destructive = false)
    {
        var dialog = new MessageDialog();
        dialog.Configure(title, message, kind, primaryText, secondaryText, destructive);

        if (owner is { IsVisible: true } && owner != dialog)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    private void Configure(string title, string message, DialogKind kind, string primaryText, string? secondaryText, bool destructive)
    {
        HeadingText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (secondaryText is not null)
        {
            SecondaryButton.Content = secondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            // A single button closes on Escape as well.
            PrimaryButton.IsCancel = true;
        }

        if (destructive)
        {
            PrimaryButton.SetResourceReference(StyleProperty, "Button.Danger");

            // Destructive confirmations default to the safe choice.
            if (secondaryText is not null)
            {
                PrimaryButton.IsDefault = false;
                SecondaryButton.IsDefault = true;
                Loaded += (_, _) => SecondaryButton.Focus();
            }
        }

        var (icon, brush) = kind switch
        {
            DialogKind.Warning => ("Icon.Warning", "Brush.Log.Warning"),
            DialogKind.Error => ("Icon.Error", "Brush.Log.Error"),
            DialogKind.Question => ("Icon.Question", "Brush.Log.Command"),
            _ => ("Icon.Info", "Brush.Log.Command"),
        };
        KindIcon.SetResourceReference(Controls.IconView.GeometryProperty, icon);
        KindIcon.SetResourceReference(ForegroundProperty, brush);
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ViewModels.ConsoleViewModel.TrySetClipboard($"{HeadingText.Text}{Environment.NewLine}{MessageText.Text}");
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}

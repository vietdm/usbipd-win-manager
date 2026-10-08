using System.Windows;
using System.Windows.Input;

namespace UsbipdManager.Controls;

/// <summary>
/// Tracks per window whether the user is navigating with the keyboard. WPF also draws the focus visual when focus
/// lands on a button without keyboard navigation (focus restored on window activation, Windows "keyboard cues"
/// setting), which left a ring on buttons that were only clicked. Styles hide their FocusVisualStyle while this is false.
/// </summary>
public static class FocusCues
{
    public static readonly DependencyProperty IsKeyboardModeProperty =
        DependencyProperty.RegisterAttached("IsKeyboardMode", typeof(bool), typeof(FocusCues),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsKeyboardMode(DependencyObject element) => (bool)element.GetValue(IsKeyboardModeProperty);

    public static void SetIsKeyboardMode(DependencyObject element, bool value) => element.SetValue(IsKeyboardModeProperty, value);

    /// <summary>Call once at startup; covers every window of the app.</summary>
    public static void Register()
    {
        // Preview events run before KeyboardNavigation moves focus, so the style is already switched when focus arrives.
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), handledEventsToo: true);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down)
        {
            SetIsKeyboardMode((Window)sender, true);
        }
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => SetIsKeyboardMode((Window)sender, false);
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UsbipdManager.Interop;
using UsbipdManager.Theme;

namespace UsbipdManager.Controls;

/// <summary>
/// Behavior for windows that use the custom chrome style ("Window.Chrome" in Theme/Controls.xaml):
/// wires the caption Close command and applies DWM rounded corners, border color and dark mode on Windows 11.
/// </summary>
public static class UiChrome
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(UiChrome), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Shows the (always disabled) minimize and maximize caption buttons.</summary>
    public static readonly DependencyProperty ShowMinMaxProperty =
        DependencyProperty.RegisterAttached("ShowMinMax", typeof(bool), typeof(UiChrome), new FrameworkPropertyMetadata(false));

    /// <summary>Set when DWM draws the rounded frame, so the template drops its own 1 px border.</summary>
    public static readonly DependencyProperty UsesSystemFrameProperty =
        DependencyProperty.RegisterAttached("UsesSystemFrame", typeof(bool), typeof(UiChrome), new FrameworkPropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetShowMinMax(DependencyObject element) => (bool)element.GetValue(ShowMinMaxProperty);

    public static void SetShowMinMax(DependencyObject element, bool value) => element.SetValue(ShowMinMaxProperty, value);

    public static bool GetUsesSystemFrame(DependencyObject element) => (bool)element.GetValue(UsesSystemFrameProperty);

    public static void SetUsesSystemFrame(DependencyObject element, bool value) => element.SetValue(UsesSystemFrameProperty, value);

    /// <summary>Re-applies the DWM frame attributes to every open chrome window (after a theme change).</summary>
    public static void RefreshAll()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (GetIsEnabled(window))
            {
                ApplyFrame(window);
            }
        }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => window.Close()));
        window.SourceInitialized += (_, _) => ApplyFrame(window);
    }

    private static void ApplyFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindows11OrLater)
        {
            return;
        }

        try
        {
            var dark = ThemeManager.IsDarkApplied ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            var corner = NativeMethods.DWMWCP_ROUND;
            var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            if (window.TryFindResource("Color.WindowBorder") is Color border)
            {
                // COLORREF is 0x00BBGGRR.
                var colorRef = border.R | (border.G << 8) | (border.B << 16);
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
            }

            SetUsesSystemFrame(window, hr == 0);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}

using System.Windows;

namespace UsbipdManager.Controls;

/// <summary>Attached properties used by the control templates in Theme/Controls.xaml.</summary>
public static class UiProps
{
    /// <summary>Marks a mode button as the current mode, or a disclosure header as expanded.</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(UiProps), new FrameworkPropertyMetadata(false));

    /// <summary>Icon geometry shown by buttons whose template has an icon slot.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(System.Windows.Media.Geometry), typeof(UiProps), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IconFilledProperty =
        DependencyProperty.RegisterAttached("IconFilled", typeof(bool), typeof(UiProps), new FrameworkPropertyMetadata(false));

    /// <summary>Hint text shown by the search box while it is empty.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(UiProps), new FrameworkPropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    public static System.Windows.Media.Geometry? GetIcon(DependencyObject element) => (System.Windows.Media.Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, System.Windows.Media.Geometry? value) => element.SetValue(IconProperty, value);

    public static bool GetIconFilled(DependencyObject element) => (bool)element.GetValue(IconFilledProperty);

    public static void SetIconFilled(DependencyObject element, bool value) => element.SetValue(IconFilledProperty, value);
}

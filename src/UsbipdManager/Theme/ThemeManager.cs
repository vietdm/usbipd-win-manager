using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using UsbipdManager.Controls;
using ThemeMode = UsbipdManager.Core.Models.ThemeMode;

namespace UsbipdManager.Theme;

/// <summary>
/// Applies System / Light / Dark by swapping the color dictionary merged into the application resources.
/// System follows HKCU\...\Themes\Personalize\AppsUseLightTheme and reacts to <see cref="SystemEvents.UserPreferenceChanged"/>.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Uri LightUri = new("pack://application:,,,/UsbipdManager;component/Theme/Colors.Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/UsbipdManager;component/Theme/Colors.Dark.xaml", UriKind.Absolute);

    private readonly Application _app;
    private bool _subscribed;

    public ThemeManager(Application app)
    {
        _app = app;
    }

    /// <summary>Raised on the UI thread after the colors changed.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>The effective theme of the running app (read by window chrome before any instance exists).</summary>
    public static bool IsDarkApplied { get; private set; }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool IsDark => IsDarkApplied;

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        if (mode == ThemeMode.System && !_subscribed)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _subscribed = true;
        }
        else if (mode != ThemeMode.System && _subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
        }

        ApplyColors(mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark(),
        });
    }

    public Color GetColor(string key) =>
        _app.TryFindResource(key) is Color color ? color : Colors.Magenta;

    public void Dispose()
    {
        if (_subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
        }
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ApplyColors(bool dark)
    {
        var dictionaries = _app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source is { } source
            && source.OriginalString.Contains("Theme/Colors.",StringComparison.OrdinalIgnoreCase));

        if (existing is not null && IsDarkApplied == dark && existing.Source == (dark ? DarkUri : LightUri))
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        IsDarkApplied = dark;
        UiChrome.RefreshAll();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // SystemEvents raises on its own thread.
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (Mode == ThemeMode.System)
            {
                ApplyColors(IsSystemDark());
            }
        });
    }
}

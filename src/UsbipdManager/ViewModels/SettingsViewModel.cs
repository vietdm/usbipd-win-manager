using System.Diagnostics;
using System.IO;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;
using UsbipdManager.Mvvm;
using UsbipdManager.Services;
using UsbipdManager.Theme;

namespace UsbipdManager.ViewModels;

public sealed record ThemeOption(ThemeMode Value, string Label);

/// <summary><see cref="Value"/> null means the default WSL distribution.</summary>
public sealed record DistroOption(string? Value, string Label);

/// <summary>
/// Settings window (PLAN section 7). Switches whose state lives outside settings.json (logon task, shortcuts) are bound
/// one-way and changed through commands, so a failure simply re-reads the real state.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IAutoStartManager _autoStart;
    private readonly IShortcutManager _shortcuts;
    private readonly ILogService _log;
    private readonly ThemeManager _theme;
    private readonly IDialogService _dialogs;

    private bool _startWithWindows;
    private bool _autoStartKnown;
    private bool _shortcutsKnown;
    private bool _autoStartBusy;
    private bool _desktopShortcut;
    private bool _startMenuShortcut;
    private ThemeOption _selectedTheme;
    private DistroOption _selectedDistro;

    public SettingsViewModel(
        ISettingsStore settings,
        IAutoStartManager autoStart,
        IShortcutManager shortcuts,
        ILogService log,
        IAppController controller,
        ThemeManager theme,
        IDialogService dialogs)
    {
        _settings = settings;
        _autoStart = autoStart;
        _shortcuts = shortcuts;
        _log = log;
        _theme = theme;
        _dialogs = dialogs;

        var current = settings.Current;

        ThemeOptions =
        [
            new ThemeOption(ThemeMode.System, "System"),
            new ThemeOption(ThemeMode.Light, "Light"),
            new ThemeOption(ThemeMode.Dark, "Dark"),
        ];
        _selectedTheme = ThemeOptions.First(o => o.Value == current.Theme);

        var distros = new List<DistroOption> { new(null, "Default") };
        foreach (var distro in controller.Report?.Distros.Where(d => d.Version == 2) ?? [])
        {
            distros.Add(new DistroOption(distro.Name, distro.IsDefault ? $"{distro.Name} (default)" : distro.Name));
        }

        if (current.WslDistribution is { Length: > 0 } saved && distros.All(d => !string.Equals(d.Value, saved, StringComparison.OrdinalIgnoreCase)))
        {
            distros.Add(new DistroOption(saved, $"{saved} (not found)"));
        }

        DistroOptions = distros;
        _selectedDistro = distros.FirstOrDefault(d => string.Equals(d.Value, current.WslDistribution, StringComparison.OrdinalIgnoreCase)) ?? distros[0];

        ToggleAutoStartCommand = new AsyncCommand(ToggleAutoStartAsync, () => _autoStartKnown && !_autoStartBusy, ex => _log.Error($"Start with Windows: {ex.Message}"));
        ToggleDesktopShortcutCommand = new AsyncCommand(() => ToggleShortcutAsync(ShortcutLocation.Desktop), () => _shortcutsKnown, ex => _log.Error($"Desktop shortcut: {ex.Message}"));
        ToggleStartMenuShortcutCommand = new AsyncCommand(() => ToggleShortcutAsync(ShortcutLocation.StartMenu), () => _shortcutsKnown, ex => _log.Error($"Start Menu shortcut: {ex.Message}"));
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<DistroOption> DistroOptions { get; }

    public AsyncCommand ToggleAutoStartCommand { get; }

    public AsyncCommand ToggleDesktopShortcutCommand { get; }

    public AsyncCommand ToggleStartMenuShortcutCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

    public bool StartWithWindows => _startWithWindows;

    public string AutoStartStatus => !_autoStartKnown
        ? "Reading the scheduled task..."
        : _autoStartBusy
            ? "Updating the scheduled task..."
            : "Starts hidden in the tray at logon, elevated without a UAC prompt.";

    public bool DesktopShortcut => _desktopShortcut;

    public bool StartMenuShortcut => _startMenuShortcut;

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !SetProperty(ref _selectedTheme, value))
            {
                return;
            }

            _theme.Apply(value.Value);
            Save(s => s.Theme = value.Value);
        }
    }

    public bool RestoreLastMode
    {
        get => _settings.Current.RestoreLastModeOnStartup;
        set => Save(s => s.RestoreLastModeOnStartup = value);
    }

    public bool AutoReattach
    {
        get => _settings.Current.AutoReattach;
        set => Save(s => s.AutoReattach = value);
    }

    public bool TrayNotifications
    {
        get => _settings.Current.TrayNotifications;
        set => Save(s => s.TrayNotifications = value);
    }

    public DistroOption SelectedDistro
    {
        get => _selectedDistro;
        set
        {
            if (value is null || !SetProperty(ref _selectedDistro, value))
            {
                return;
            }

            Save(s => s.WslDistribution = value.Value);
        }
    }

    /// <summary>Reads the states that live outside settings.json. Called when the window opens.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var (desktop, startMenu) = await Task.Run(() => (_shortcuts.Exists(ShortcutLocation.Desktop), _shortcuts.Exists(ShortcutLocation.StartMenu)));
            _desktopShortcut = desktop;
            _startMenuShortcut = startMenu;
            OnPropertyChanged(nameof(DesktopShortcut));
            OnPropertyChanged(nameof(StartMenuShortcut));
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read the shortcut state: {ex.Message}");
        }

        _shortcutsKnown = true;
        ToggleDesktopShortcutCommand.RaiseCanExecuteChanged();
        ToggleStartMenuShortcutCommand.RaiseCanExecuteChanged();

        try
        {
            _startWithWindows = await Task.Run(() => _autoStart.IsEnabledAsync());
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read the Start with Windows state: {ex.Message}");
        }

        _autoStartKnown = true;
        RaiseAutoStart();
    }

    private async Task ToggleAutoStartAsync()
    {
        var enable = !_startWithWindows;
        _autoStartBusy = true;
        RaiseAutoStart();
        try
        {
            var result = await Task.Run(() => enable ? _autoStart.EnableAsync() : _autoStart.DisableAsync());
            if (result.Success)
            {
                _startWithWindows = enable;
            }
            else
            {
                _dialogs.ShowMessage("Start with Windows", result.Error ?? "The scheduled task could not be changed.", DialogKind.Error);
            }
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("Start with Windows", ex.Message, DialogKind.Error);
        }
        finally
        {
            _autoStartBusy = false;
            RaiseAutoStart();
        }
    }

    private async Task ToggleShortcutAsync(ShortcutLocation location)
    {
        var exists = location == ShortcutLocation.Desktop ? _desktopShortcut : _startMenuShortcut;
        var title = location == ShortcutLocation.Desktop ? "Desktop shortcut" : "Start Menu shortcut";
        try
        {
            var result = await Task.Run(() => exists ? _shortcuts.Remove(location) : _shortcuts.Create(location));
            if (!result.Success)
            {
                _dialogs.ShowMessage(title, result.Error ?? "The shortcut could not be changed.", DialogKind.Error);
            }
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage(title, ex.Message, DialogKind.Error);
        }

        var now = await Task.Run(() => _shortcuts.Exists(location));
        if (location == ShortcutLocation.Desktop)
        {
            _desktopShortcut = now;
        }
        else
        {
            _startMenuShortcut = now;
        }

        // Raised even when unchanged, so a failed toggle snaps back.
        OnPropertyChanged(location == ShortcutLocation.Desktop ? nameof(DesktopShortcut) : nameof(StartMenuShortcut));
    }

    private void OpenLogFolder()
    {
        try
        {
            var directory = _log.LogDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("Open log folder", ex.Message, DialogKind.Error);
        }
    }

    private void Save(Action<AppSettings> mutate, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        try
        {
            _settings.Update(mutate);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("Settings", $"The settings could not be saved: {ex.Message}", DialogKind.Error);
        }

        OnPropertyChanged(propertyName);
    }

    private void RaiseAutoStart()
    {
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(AutoStartStatus));
        ToggleAutoStartCommand.RaiseCanExecuteChanged();
    }
}

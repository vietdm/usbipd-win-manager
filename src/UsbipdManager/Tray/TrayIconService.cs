using System.Windows;
using System.Windows.Threading;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;
using UsbipdManager.Theme;
using UsbipdManager.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UsbipdManager.Tray;

/// <summary>
/// Tray icon (per state), tooltip, themed context menu and balloon notifications.
/// Created and used on the UI thread; state comes from <see cref="MainViewModel.StateRefreshed"/>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string AppName = "USBIPD Manager";
    private static readonly TimeSpan ErrorBalloonInterval = TimeSpan.FromSeconds(15);

    private readonly MainViewModel _viewModel;
    private readonly ISettingsStore _settings;
    private readonly ILogService _log;
    private readonly ThemeManager _theme;
    private readonly Dispatcher _dispatcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _windowsItem;
    private readonly Forms.ToolStripMenuItem _wslItem;
    private readonly Forms.ToolStripMenuItem _aboutItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private readonly Drawing.Icon _windowsIcon;
    private readonly Drawing.Icon _wslIcon;
    private readonly Drawing.Icon _warningIcon;
    private readonly Drawing.Font _menuFont;
    private readonly Drawing.Font _menuFontBold;

    private UsbMode? _lastMode;
    private DateTime _lastErrorBalloon = DateTime.MinValue;
    private bool _hiddenHintShown;
    private bool _exiting;
    private bool _disposed;

    public TrayIconService(
        MainViewModel viewModel,
        ISettingsStore settings,
        ILogService log,
        ThemeManager theme,
        Dispatcher dispatcher,
        Action open,
        Action about,
        Action exit)
    {
        _viewModel = viewModel;
        _settings = settings;
        _log = log;
        _theme = theme;
        _dispatcher = dispatcher;

        var size = Forms.SystemInformation.SmallIconSize;
        _windowsIcon = LoadIcon("tray-windows.ico", size);
        _wslIcon = LoadIcon("tray-wsl.ico", size);
        _warningIcon = LoadIcon("tray-warning.ico", size);

        _menuFont = new Drawing.Font("Segoe UI", 9f);
        _menuFontBold = new Drawing.Font("Segoe UI", 9f, Drawing.FontStyle.Bold);

        _openItem = CreateItem("Open", (_, _) => open());
        _openItem.Font = _menuFontBold; // default action (double-click)
        _windowsItem = CreateItem("Switch to Windows", (_, _) => _viewModel.WindowsCommand.Execute(null));
        _wslItem = CreateItem("Switch to WSL2", (_, _) => _viewModel.WslCommand.Execute(null));
        _aboutItem = CreateItem("About", (_, _) => about());
        _exitItem = CreateItem("Exit", (_, _) => exit());

        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = _menuFont,
            Padding = new Forms.Padding(0, 4, 0, 4),
        };
        _menu.Items.AddRange(
        [
            _openItem,
            _windowsItem,
            _wslItem,
            new Forms.ToolStripSeparator(),
            _aboutItem,
            _exitItem,
        ]);
        _menu.Opening += (_, _) => Update();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _windowsIcon,
            Text = $"{AppName} - Mode: Windows",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };

        ApplyTheme();
        Update();

        _viewModel.StateRefreshed += OnStateRefreshed;
        _theme.ThemeChanged += OnThemeChanged;
        _log.EntryAdded += OnLogEntryAdded;
    }

    /// <summary>Disables the menu while the exit sequence releases devices.</summary>
    public void SetExiting()
    {
        _exiting = true;
        Update();
    }

    /// <summary>Tells the user once per session that closing the window keeps the app running.</summary>
    public void NotifyHiddenToTray()
    {
        if (_hiddenHintShown || !_settings.Current.TrayNotifications)
        {
            return;
        }

        _hiddenHintShown = true;
        ShowBalloon($"{AppName} is still running", "Double-click the tray icon to open it. Use Exit in its menu to quit.", Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.StateRefreshed -= OnStateRefreshed;
        _theme.ThemeChanged -= OnThemeChanged;
        _log.EntryAdded -= OnLogEntryAdded;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _windowsIcon.Dispose();
        _wslIcon.Dispose();
        _warningIcon.Dispose();
        _menuFont.Dispose();
        _menuFontBold.Dispose();
    }

    private static Drawing.Icon LoadIcon(string fileName, Drawing.Size size)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/UsbipdManager;component/Assets/{fileName}", UriKind.Absolute))
            ?? throw new InvalidOperationException($"Missing tray icon resource {fileName}.");
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, size);
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(8, 4, 16, 4) };
        item.Click += onClick;
        return item;
    }

    private static Drawing.Color ToDrawing(System.Windows.Media.Color color) =>
        Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    private void OnStateRefreshed(object? sender, EventArgs e) => Update();

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        var palette = new TrayPalette(
            ToDrawing(_theme.GetColor("Color.Surface")),
            ToDrawing(_theme.GetColor("Color.Text")),
            ToDrawing(_theme.GetColor("Color.TextDisabled")),
            ToDrawing(_theme.GetColor("Color.SurfaceHover")),
            ToDrawing(_theme.GetColor("Color.BorderStrong")));
        _menu.Renderer = new TrayMenuRenderer(palette);
        _menu.BackColor = palette.Surface;
        _menu.ForeColor = palette.Text;
    }

    private void Update()
    {
        if (_disposed)
        {
            return;
        }

        var mode = _viewModel.Mode;
        var report = _viewModel.Report;
        var modeName = mode == UsbMode.Wsl ? "WSL2" : "Windows";

        var problem = report switch
        {
            null => null,
            { IsSupported: false } => "Not supported: WSL is not installed",
            { UsbipdInstalled: false } => "usbipd-win is not installed",
            { IsUsbipdReady: false } => "The usbipd service is not running",
            _ => null,
        };

        _notifyIcon.Icon = problem is not null ? _warningIcon : mode == UsbMode.Wsl ? _wslIcon : _windowsIcon;
        var text = _exiting ? $"{AppName} - Exiting..." : $"{AppName} - Mode: {modeName}";
        if (problem is not null && !_exiting)
        {
            text += $"\n{problem}";
        }

        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;

        _windowsItem.Enabled = _viewModel.CanSwitchToWindows && !_exiting;
        _wslItem.Enabled = _viewModel.CanSwitchToWsl && !_exiting;
        _openItem.Enabled = !_exiting;
        _aboutItem.Enabled = !_exiting;
        _exitItem.Enabled = !_exiting;

        if (_lastMode is { } previous && previous != mode && !_exiting && _settings.Current.TrayNotifications)
        {
            ShowBalloon($"Mode: {modeName}", mode == UsbMode.Wsl
                ? "Managed devices are attached to WSL2."
                : "Managed devices are back on Windows.", Forms.ToolTipIcon.Info);
        }

        _lastMode = mode;
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        if (entry.Level != LogLevel.Error)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || _exiting || !_settings.Current.TrayNotifications || DateTime.UtcNow - _lastErrorBalloon < ErrorBalloonInterval)
            {
                return;
            }

            _lastErrorBalloon = DateTime.UtcNow;
            var message = entry.Message.Length > 200 ? entry.Message[..200] + "..." : entry.Message;
            ShowBalloon($"{AppName}: error", message, Forms.ToolTipIcon.Error);
        });
    }

    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, icon);
        }
        catch (Exception)
        {
            // Notifications are best effort (e.g. Focus Assist or a disposed shell icon).
        }
    }
}

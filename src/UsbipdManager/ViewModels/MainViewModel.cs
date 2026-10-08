using System.Collections.ObjectModel;
using System.Windows.Threading;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;
using UsbipdManager.Mvvm;
using UsbipdManager.Services;

namespace UsbipdManager.ViewModels;

/// <summary>
/// State of the main window and the tray menu. Controller events arrive on background threads and are coalesced
/// into one UI-thread refresh. "Busy" here means a user-initiated operation (the controller's own periodic refreshes
/// also hold the gate briefly, which must not make the UI blink).
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAppController _controller;
    private readonly ILogService _log;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private readonly Action _openSettings;
    private int _refreshScheduled;
    private int _userOperations;
    private string _busyText = "Working...";
    private bool _isExiting;

    private UsbMode _mode;
    private EnvironmentReport? _report;

    public MainViewModel(IAppController controller, ILogService log, IAppInfo appInfo, IDialogService dialogs, Dispatcher dispatcher, Action openSettings)
    {
        _controller = controller;
        _log = log;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _openSettings = openSettings;
        Copyright = appInfo.Copyright;

        Console = new ConsoleViewModel(log, dispatcher);

        InitCommand = new AsyncCommand(() => RunUserOperationAsync("Running checks and fixes...", ct => _controller.InitAsync(ct)), () => CanInit, OnCommandError);
        WindowsCommand = new AsyncCommand(SwitchToWindowsAsync, () => CanSwitch, OnCommandError);
        WslCommand = new AsyncCommand(SwitchToWslAsync, () => CanSwitch, OnCommandError);
        RefreshDevicesCommand = new AsyncCommand(() => RunUserOperationAsync("Refreshing devices...", ct => _controller.RefreshDevicesAsync(ct)), () => CanInit, OnCommandError);
        SettingsCommand = new RelayCommand(() => _openSettings(), () => !_isExiting);
        HelpCommand = new RelayCommand(() => _dialogs.ShowMessage("Help", "Send issues to email vietdau33@gmail.com", DialogKind.Info));

        _controller.StateChanged += OnControllerStateChanged;
        Refresh();
    }

    /// <summary>Raised on the UI thread after the state was refreshed (the tray listens to it).</summary>
    public event EventHandler? StateRefreshed;

    public ConsoleViewModel Console { get; }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public string Copyright { get; }

    public AsyncCommand InitCommand { get; }

    public AsyncCommand WindowsCommand { get; }

    public AsyncCommand WslCommand { get; }

    public AsyncCommand RefreshDevicesCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand HelpCommand { get; }

    public UsbMode Mode => _mode;

    public EnvironmentReport? Report => _report;

    public string ModeText => _mode == UsbMode.Wsl ? "Mode: WSL2" : "Mode: Windows";

    public bool IsWindowsActive => _mode == UsbMode.Windows;

    public bool IsWslActive => _mode == UsbMode.Wsl;

    /// <summary>A user-initiated operation (or the exit sequence) is running.</summary>
    public bool IsBusy => _userOperations > 0 || _isExiting;

    public string BusyText => _isExiting ? "Returning devices to Windows..." : _busyText;

    public bool IsExiting => _isExiting;

    public bool IsReady => _report?.CanSwitch == true;

    public bool CanSwitch => IsReady && !IsBusy;

    public bool CanInit => !IsBusy;

    public bool IsChecking => _report is null;

    public bool IsUnsupported => _report is { IsSupported: false };

    public bool IsUsbipdNotReady => _report is { IsSupported: true, IsUsbipdReady: false };

    public string UsbipdWarningText => _report is { UsbipdInstalled: false }
        ? "usbipd-win is not installed. Press Init to install it."
        : "The usbipd service is not running. Press Init to start it.";

    public string StatusDetail
    {
        get
        {
            if (_report is null)
            {
                return "Checking environment...";
            }

            var usbipd = _report.UsbipdInstalled ? $"usbipd {_report.UsbipdVersion ?? "installed"}" : "usbipd missing";
            var distro = _report.Distros.FirstOrDefault(d => d.IsDefault && d.Version == 2) ?? _report.Distros.FirstOrDefault(d => d.Version == 2);
            var wsl = !_report.WslInstalled ? "WSL missing" : distro is null ? "no WSL2 distro" : $"WSL2: {distro.Name}";
            return $"{usbipd}  |  {wsl}";
        }
    }

    public bool HasDevices => Devices.Count > 0;

    public string EmptyStateText
    {
        get
        {
            if (_report is null)
            {
                return "Looking for USB devices...";
            }

            if (!_report.UsbipdInstalled)
            {
                return "usbipd-win is not installed, so devices cannot be listed. Press Init to install it.";
            }

            return "No USB devices found. Plug in a device to see it here.";
        }
    }

    public void SetExiting()
    {
        _isExiting = true;
        RaiseAll();
    }

    public Task SwitchToWindowsAsync() =>
        RunUserOperationAsync("Moving devices to Windows...", ct => _controller.SwitchToWindowsAsync(ct));

    public Task SwitchToWslAsync() =>
        RunUserOperationAsync("Moving devices to WSL2...", ct => _controller.SwitchToWslAsync(ct));

    public async Task ToggleManagedAsync(DeviceItemViewModel item)
    {
        var desired = !item.IsManaged;
        if (desired && item.IsInputLike)
        {
            var confirmed = _dialogs.Confirm(
                "Input device",
                $"{item.Description} looks like an input device. Moving it to WSL2 disconnects it from Windows. Continue?",
                "Continue",
                "Cancel",
                DialogKind.Warning,
                destructive: true);
            if (!confirmed)
            {
                item.RevertToggle();
                return;
            }
        }

        item.IsPending = true;
        try
        {
            var entry = item.Entry;
            await RunUserOperationAsync(desired ? "Sharing device..." : "Releasing device...", ct => _controller.SetManagedAsync(entry, desired, ct));
        }
        finally
        {
            item.IsPending = false;
            item.RevertToggle();
        }
    }

    public async Task ForgetAsync(DeviceItemViewModel item)
    {
        item.IsPending = true;
        try
        {
            var entry = item.Entry;
            await RunUserOperationAsync("Forgetting device...", ct => _controller.ForgetAsync(entry, ct));
        }
        finally
        {
            item.IsPending = false;
        }
    }

    public void Dispose()
    {
        _controller.StateChanged -= OnControllerStateChanged;
        Console.Dispose();
    }

    private async Task RunUserOperationAsync(string busyText, Func<CancellationToken, Task> operation)
    {
        _userOperations++;
        _busyText = busyText;
        RaiseAll();
        try
        {
            // Task.Run: the controller must never block the UI thread, even before its first await.
            await Task.Run(() => operation(CancellationToken.None));
        }
        finally
        {
            _userOperations--;
            Refresh();
        }
    }

    private void OnCommandError(Exception ex) => _log.Error($"Unexpected error: {ex.Message}");

    private void OnControllerStateChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 0)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                Interlocked.Exchange(ref _refreshScheduled, 0);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        _mode = _controller.Mode;
        _report = _controller.Report;
        SyncDevices(_controller.Devices);
        RaiseAll();
    }

    private void SyncDevices(IReadOnlyList<DeviceEntry> entries)
    {
        var available = Devices.ToList();
        var ordered = new List<DeviceItemViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            var match = available.FirstOrDefault(vm => vm.Key == entry.DeviceKey && vm.Entry.BusId == entry.BusId)
                ?? available.FirstOrDefault(vm => vm.Key == entry.DeviceKey);
            if (match is null)
            {
                match = new DeviceItemViewModel(entry, this);
            }
            else
            {
                available.Remove(match);
                match.Update(entry);
            }

            ordered.Add(match);
        }

        foreach (var stale in available)
        {
            Devices.Remove(stale);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Devices.IndexOf(ordered[i]);
            if (current < 0)
            {
                Devices.Insert(i, ordered[i]);
            }
            else if (current != i)
            {
                Devices.Move(current, i);
            }
        }
    }

    private void RaiseAll()
    {
        foreach (var item in Devices)
        {
            item.CanInteract = CanSwitch;
        }

        OnPropertyChanged(string.Empty);
        InitCommand.RaiseCanExecuteChanged();
        WindowsCommand.RaiseCanExecuteChanged();
        WslCommand.RaiseCanExecuteChanged();
        RefreshDevicesCommand.RaiseCanExecuteChanged();
        SettingsCommand.RaiseCanExecuteChanged();
        StateRefreshed?.Invoke(this, EventArgs.Empty);
    }
}

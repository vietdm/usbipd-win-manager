using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

/// <summary>
/// Every public action runs through the gate and never throws (except cancellation through the caller's token).
/// Device events are debounced; a refresh that finds the gate busy runs right after the current operation.
/// <see cref="StateChanged"/> is raised on any thread.
/// </summary>
public sealed class AppController : IAppController, IDisposable
{
    internal static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan DefaultPeriodicRefresh = TimeSpan.FromSeconds(30);

    private readonly IEnvironmentChecker _checker;
    private readonly IInitService _init;
    private readonly IDeviceManager _devices;
    private readonly IModeSwitcher _modes;
    private readonly IDeviceChangeNotifier _deviceChanges;
    private readonly IOperationGate _gate;
    private readonly ISettingsStore _settings;
    private readonly ILogService _log;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _periodicRefresh;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _timerLock = new();
    private readonly Timer _debounceTimer;

    private Timer? _periodicTimer;
    private volatile EnvironmentReport? _report;
    private volatile bool _stopped;
    private int _started;
    private int _pendingRefresh;
    private bool _listeningToDevices;

    public AppController(
        IEnvironmentChecker checker,
        IInitService init,
        IDeviceManager devices,
        IModeSwitcher modes,
        IDeviceChangeNotifier deviceChanges,
        IOperationGate gate,
        ISettingsStore settings,
        ILogService log)
        : this(checker, init, devices, modes, deviceChanges, gate, settings, log, DefaultDebounce, DefaultPeriodicRefresh)
    {
    }

    internal AppController(
        IEnvironmentChecker checker,
        IInitService init,
        IDeviceManager devices,
        IModeSwitcher modes,
        IDeviceChangeNotifier deviceChanges,
        IOperationGate gate,
        ISettingsStore settings,
        ILogService log,
        TimeSpan debounce,
        TimeSpan periodicRefresh)
    {
        _checker = checker;
        _init = init;
        _devices = devices;
        _modes = modes;
        _deviceChanges = deviceChanges;
        _gate = gate;
        _settings = settings;
        _log = log;
        _debounce = debounce;
        _periodicRefresh = periodicRefresh;
        _debounceTimer = new Timer(_ => OnDebounceElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _gate.BusyChanged += OnInnerStateChanged;
        _settings.Changed += OnInnerStateChanged;
        _devices.EntriesChanged += OnInnerStateChanged;
    }

    public event EventHandler? StateChanged;

    public EnvironmentReport? Report => _report;

    public UsbMode Mode => _settings.Current.Mode;

    public bool IsBusy => _gate.IsBusy;

    public bool CanSwitch => _report?.CanSwitch == true && !_gate.IsBusy;

    public IReadOnlyList<DeviceEntry> Devices => _devices.Entries;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return RunGuardedAsync(
            async ct =>
            {
                SetReport(await _checker.CheckAsync(ct).ConfigureAwait(false));
                StartMonitoring();
                await RestoreModeAsync(ct).ConfigureAwait(false);
                await _devices.RefreshAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task InitAsync(CancellationToken cancellationToken = default) =>
        RunGuardedAsync(
            async ct =>
            {
                SetReport(await _init.RunAsync(ct).ConfigureAwait(false));
                await _devices.RefreshAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);

    public Task SwitchToWindowsAsync(CancellationToken cancellationToken = default) =>
        RunGuardedAsync(
            async ct =>
            {
                if (!EnsureCanSwitch("switch to Windows"))
                {
                    return;
                }

                await _modes.SwitchToWindowsAsync(ct).ConfigureAwait(false);
                await _devices.RefreshAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);

    public Task SwitchToWslAsync(CancellationToken cancellationToken = default) =>
        RunGuardedAsync(
            async ct =>
            {
                if (!EnsureCanSwitch("switch to WSL2"))
                {
                    return;
                }

                await _modes.SwitchToWslAsync(ct).ConfigureAwait(false);
                await _devices.RefreshAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);

    public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) =>
        RunGuardedAsync(ct => _devices.RefreshAsync(ct), cancellationToken);

    public Task SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RunGuardedAsync(
            async ct =>
            {
                // Turning off a disconnected entry only forgets it, which needs neither usbipd nor WSL.
                var needsUsbipd = managed || entry.IsConnected;
                if (needsUsbipd && !EnsureCanSwitch($"turn {(managed ? "on" : "off")} {entry.Description}"))
                {
                    return;
                }

                await _devices.SetManagedAsync(entry, managed, ct).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task ForgetAsync(DeviceEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RunGuardedAsync(
            _ =>
            {
                _devices.Forget(entry);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        StopMonitoring();
        try
        {
            await _gate.RunAsync(
                async ct =>
                {
                    if (_report?.UsbipdInstalled != true)
                    {
                        _log.Info("USBIPD Manager is exiting.");
                        return true;
                    }

                    var release = await _modes.ReleaseForExitAsync(ct).ConfigureAwait(false);
                    if (release.Success)
                    {
                        _log.Info("USBIPD Manager is exiting. Devices were returned to Windows.");
                    }
                    else
                    {
                        _log.Warning("USBIPD Manager is exiting. Some devices may still be attached to WSL2.");
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Unexpected error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopTimersAndEvents();
        _gate.BusyChanged -= OnInnerStateChanged;
        _settings.Changed -= OnInnerStateChanged;
        _devices.EntriesChanged -= OnInnerStateChanged;
        _debounceTimer.Dispose();
    }

    /// <summary>Refresh from a device event or the periodic timer: skipped when busy, then run after the current operation.</summary>
    internal async Task BackgroundRefreshAsync()
    {
        if (_stopped)
        {
            return;
        }

        bool ran;
        try
        {
            ran = await _gate.TryRunAsync(ct => _devices.RefreshAsync(ct), _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _log.Error($"Unexpected error: {ex.Message}");
            ran = true;
        }

        if (ran)
        {
            RunPendingRefresh();
            return;
        }

        Volatile.Write(ref _pendingRefresh, 1);

        // The operation may have finished between the failed attempt and setting the flag.
        if (!_gate.IsBusy)
        {
            RunPendingRefresh();
        }
    }

    private async Task RunGuardedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.RunAsync(
                async ct =>
                {
                    await action(ct).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Unexpected error: {ex.Message}");
        }
        finally
        {
            RunPendingRefresh();
            RaiseStateChanged();
        }
    }

    private async Task RestoreModeAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (settings.Mode != UsbMode.Wsl)
        {
            _log.Info("Mode: Windows.");
            return;
        }

        if (!settings.RestoreLastModeOnStartup)
        {
            _settings.Update(s => s.Mode = UsbMode.Windows);
            _log.Info("Mode: Windows.");
            return;
        }

        var report = _report;
        if (report?.CanSwitch != true)
        {
            // The saved mode is kept: once Init fixes the machine, managed devices are attached on the next refresh.
            _log.Warning($"WSL2 mode could not be restored yet. {WhyCannotSwitch(report)}");
            return;
        }

        _log.Info("Restoring WSL2 mode...");
        await _modes.SwitchToWslAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool EnsureCanSwitch(string action)
    {
        var report = _report;
        if (report?.CanSwitch == true)
        {
            return true;
        }

        _log.Error($"Cannot {action}. {WhyCannotSwitch(report)}");
        return false;
    }

    private static string WhyCannotSwitch(EnvironmentReport? report)
    {
        if (report is null)
        {
            return "The environment has not been checked yet. Press Init.";
        }

        if (!report.WslInstalled)
        {
            return "WSL is not installed on this machine. USBIPD Manager is not supported.";
        }

        if (!report.HasWsl2Distro)
        {
            return "No WSL 2 distribution found. USBIPD Manager is not supported.";
        }

        if (!report.UsbipdInstalled)
        {
            return "usbipd-win is not installed. Press Init to install it.";
        }

        return report.UsbipdService.IsRunning
            ? "The environment is not ready. Press Init."
            : "usbipd service is not running. Press Init to start it.";
    }

    private void SetReport(EnvironmentReport report)
    {
        _report = report;
        RaiseStateChanged();
    }

    private void StartMonitoring()
    {
        lock (_timerLock)
        {
            if (_stopped || _listeningToDevices)
            {
                return;
            }

            _deviceChanges.DeviceChanged += OnDeviceChanged;
            _listeningToDevices = true;
            _periodicTimer = new Timer(_ => OnPeriodicTick(), null, _periodicRefresh, _periodicRefresh);
        }

        try
        {
            _deviceChanges.Start();
        }
        catch (Exception ex)
        {
            _log.Warning($"Device change events are not available ({ex.Message}). The device list refreshes every {_periodicRefresh.TotalSeconds:0} s.");
        }
    }

    private void StopMonitoring()
    {
        bool wasListening;
        lock (_timerLock)
        {
            wasListening = _listeningToDevices;
        }

        StopTimersAndEvents();
        if (!wasListening)
        {
            return;
        }

        try
        {
            _deviceChanges.Stop();
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not stop device monitoring: {ex.Message}");
        }
    }

    private void StopTimersAndEvents()
    {
        _stopped = true;
        _lifetime.Cancel();
        lock (_timerLock)
        {
            TryChange(_debounceTimer, Timeout.InfiniteTimeSpan);
            _periodicTimer?.Dispose();
            _periodicTimer = null;
            if (_listeningToDevices)
            {
                _deviceChanges.DeviceChanged -= OnDeviceChanged;
                _listeningToDevices = false;
            }
        }
    }

    private void OnDeviceChanged(object? sender, EventArgs e)
    {
        lock (_timerLock)
        {
            if (!_stopped)
            {
                TryChange(_debounceTimer, _debounce);
            }
        }
    }

    private static void TryChange(Timer timer, TimeSpan dueTime)
    {
        try
        {
            timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed during shutdown.
        }
    }

    private void OnDebounceElapsed()
    {
        if (!_stopped && _report?.IsUsbipdReady == true)
        {
            _ = BackgroundRefreshAsync();
        }
    }

    private void OnPeriodicTick()
    {
        if (!_stopped && _report?.IsUsbipdReady == true)
        {
            _ = BackgroundRefreshAsync();
        }
    }

    private void RunPendingRefresh()
    {
        if (!_stopped && Interlocked.Exchange(ref _pendingRefresh, 0) == 1)
        {
            _ = Task.Run(BackgroundRefreshAsync);
        }
    }

    private void OnInnerStateChanged(object? sender, EventArgs e) => RaiseStateChanged();

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

using System.Diagnostics;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Services;
using UsbipdManager.Tests.Services.Fakes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Services;

public sealed class AppControllerTests : IDisposable
{
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeEnvironmentChecker _checker = new();
    private readonly FakeInitService _init = new();
    private readonly FakeDeviceManager _devices = new();
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private readonly OperationGate _gate = new();
    private readonly TestLog _log = new();
    private readonly FakeModeSwitcher _modes;
    private AppController? _controller;

    public AppControllerTests()
    {
        _modes = new FakeModeSwitcher(_settings);
    }

    public void Dispose() => _controller?.Dispose();

    private AppController Create(TimeSpan? debounce = null, TimeSpan? periodic = null) =>
        _controller = new AppController(
            _checker,
            _init,
            _devices,
            _modes,
            _notifier,
            _gate,
            _settings,
            _log,
            debounce ?? TimeSpan.FromMilliseconds(30),
            periodic ?? Timeout.InfiniteTimeSpan);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5))
            {
                Assert.Fail("Timed out waiting for the condition.");
            }

            await Task.Delay(10);
        }
    }

    private static DeviceEntry Entry(bool connected) =>
        new("1-4", "K4", "Pixel 8", "18d1:4ee7", connected ? UsbDeviceState.Shared : null, connected, true, false);

    [Fact]
    public async Task Start_checks_monitors_restores_wsl_mode_and_refreshes()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        var controller = Create();

        await controller.StartAsync();

        Assert.Same(_checker.Report, controller.Report);
        Assert.Equal(1, _notifier.StartCalls);
        Assert.True(_notifier.HasSubscribers);
        Assert.Equal(["wsl"], _modes.Calls);
        Assert.True(_log.Contains(LogLevel.Info, "Restoring WSL2 mode..."));
        Assert.True(_devices.RefreshCalls >= 1);
        Assert.Equal(UsbMode.Wsl, controller.Mode);
        Assert.True(controller.CanSwitch);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Start_without_restore_saves_windows_mode()
    {
        _settings.Update(s =>
        {
            s.Mode = UsbMode.Wsl;
            s.RestoreLastModeOnStartup = false;
        });
        var controller = Create();

        await controller.StartAsync();

        Assert.Empty(_modes.Calls);
        Assert.Equal(UsbMode.Windows, controller.Mode);
    }

    [Fact]
    public async Task Start_in_windows_mode_does_not_switch()
    {
        var controller = Create();

        await controller.StartAsync();

        Assert.Empty(_modes.Calls);
        Assert.Equal(UsbMode.Windows, controller.Mode);
    }

    [Fact]
    public async Task Start_does_not_restore_on_an_unsupported_machine()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        _checker.Report = Reports.NoWsl();
        var controller = Create();

        await controller.StartAsync();

        Assert.Empty(_modes.Calls);
        Assert.False(controller.CanSwitch);
        Assert.True(_log.Contains(LogLevel.Warning, "WSL2 mode could not be restored yet"));
    }

    [Fact]
    public async Task Switching_is_blocked_before_the_environment_check()
    {
        var controller = Create();

        await controller.SwitchToWslAsync();

        Assert.Empty(_modes.Calls);
        Assert.True(_log.Contains(LogLevel.Error, "Cannot switch to WSL2"));
    }

    [Fact]
    public async Task Switching_is_blocked_on_an_unsupported_machine()
    {
        _checker.Report = Reports.NoWsl();
        var controller = Create();
        await controller.StartAsync();

        await controller.SwitchToWslAsync();
        await controller.SwitchToWindowsAsync();

        Assert.Empty(_modes.Calls);
        Assert.True(_log.Contains(LogLevel.Error, "Cannot switch to WSL2. WSL is not installed on this machine. USBIPD Manager is not supported."));
        Assert.True(_log.Contains(LogLevel.Error, "Cannot switch to Windows."));
    }

    [Fact]
    public async Task Switching_is_blocked_while_usbipd_is_missing()
    {
        _checker.Report = Reports.UsbipdMissing();
        var controller = Create();
        await controller.StartAsync();

        await controller.SwitchToWslAsync();

        Assert.Empty(_modes.Calls);
        Assert.True(_log.Contains(LogLevel.Error, "usbipd-win is not installed. Press Init to install it."));
    }

    [Fact]
    public async Task Switch_methods_call_the_mode_switcher_and_refresh()
    {
        var controller = Create();
        await controller.StartAsync();
        var refreshes = _devices.RefreshCalls;

        await controller.SwitchToWslAsync();
        await controller.SwitchToWindowsAsync();

        Assert.Equal(["wsl", "windows"], _modes.Calls);
        Assert.Equal(refreshes + 2, _devices.RefreshCalls);
    }

    [Fact]
    public async Task Exceptions_are_logged_and_never_thrown()
    {
        var controller = Create();
        await controller.StartAsync();
        _modes.SwitchException = new InvalidOperationException("boom");

        await controller.SwitchToWslAsync();

        Assert.True(_log.Contains(LogLevel.Error, "Unexpected error: boom"));
        Assert.False(controller.IsBusy);

        _devices.RefreshException = new InvalidOperationException("usbipd state failed");
        await controller.RefreshDevicesAsync();
        Assert.True(_log.Contains(LogLevel.Error, "Unexpected error: usbipd state failed"));
    }

    [Fact]
    public async Task Cancellation_through_the_callers_token_is_rethrown()
    {
        var controller = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.RefreshDevicesAsync(cts.Token));
    }

    [Fact]
    public async Task Init_stores_the_report_and_refreshes()
    {
        _checker.Report = Reports.UsbipdMissing();
        var controller = Create();
        await controller.StartAsync();
        Assert.False(controller.CanSwitch);
        var refreshes = _devices.RefreshCalls;

        await controller.InitAsync();

        Assert.Equal(1, _init.Calls);
        Assert.Same(_init.Report, controller.Report);
        Assert.True(controller.CanSwitch);
        Assert.Equal(refreshes + 1, _devices.RefreshCalls);
    }

    [Fact]
    public async Task Device_toggles_require_a_ready_machine_but_forgetting_does_not()
    {
        _checker.Report = Reports.NoWsl();
        var controller = Create();
        await controller.StartAsync();

        await controller.SetManagedAsync(Entry(connected: true), true);
        await controller.SetManagedAsync(Entry(connected: false), false);
        await controller.ForgetAsync(Entry(connected: false));

        var call = Assert.Single(_devices.SetManagedCalls);
        Assert.False(call.Managed);
        Assert.Single(_devices.Forgotten);
    }

    [Fact]
    public async Task Device_events_are_debounced_into_one_refresh()
    {
        var controller = Create(debounce: TimeSpan.FromMilliseconds(100));
        await controller.StartAsync();
        var before = _devices.RefreshCalls;

        for (var i = 0; i < 5; i++)
        {
            _notifier.Raise();
        }

        await WaitUntil(() => _devices.RefreshCalls > before);
        await Task.Delay(300);
        Assert.Equal(before + 1, _devices.RefreshCalls);
    }

    [Fact]
    public async Task Device_events_do_not_refresh_while_usbipd_is_not_ready()
    {
        _checker.Report = Reports.UsbipdMissing();
        var controller = Create(debounce: TimeSpan.FromMilliseconds(10));
        await controller.StartAsync();
        var before = _devices.RefreshCalls;

        _notifier.Raise();
        await Task.Delay(200);

        Assert.Equal(before, _devices.RefreshCalls);
    }

    [Fact]
    public async Task A_refresh_skipped_while_busy_runs_after_the_current_operation()
    {
        var controller = Create();
        await controller.StartAsync();
        var release = new TaskCompletionSource();
        _devices.RefreshHook = () => release.Task;
        var before = _devices.RefreshCalls;

        var running = controller.RefreshDevicesAsync();
        await WaitUntil(() => _gate.IsBusy);
        await controller.BackgroundRefreshAsync();
        Assert.Equal(before + 1, _devices.RefreshCalls);

        _devices.RefreshHook = null;
        release.SetResult();
        await running;

        await WaitUntil(() => _devices.RefreshCalls == before + 2);
    }

    [Fact]
    public async Task Periodic_refresh_runs_while_usbipd_is_ready()
    {
        var controller = Create(periodic: TimeSpan.FromMilliseconds(50));
        await controller.StartAsync();
        var before = _devices.RefreshCalls;

        await WaitUntil(() => _devices.RefreshCalls >= before + 2);
    }

    [Fact]
    public async Task StateChanged_is_raised_for_report_busy_settings_and_devices()
    {
        var controller = Create();
        var raised = 0;
        controller.StateChanged += (_, _) => Interlocked.Increment(ref raised);

        await controller.StartAsync();
        Assert.True(raised > 0);

        raised = 0;
        _settings.Update(s => s.TrayNotifications = false);
        Assert.Equal(1, raised);

        raised = 0;
        _devices.RaiseEntriesChanged();
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Shutdown_stops_monitoring_and_releases_devices_without_changing_the_mode()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        var controller = Create(debounce: TimeSpan.FromMilliseconds(10));
        await controller.StartAsync();
        var refreshes = _devices.RefreshCalls;

        await controller.ShutdownAsync();

        Assert.Equal(1, _notifier.StopCalls);
        Assert.False(_notifier.HasSubscribers);
        Assert.Equal("release", _modes.Calls[^1]);
        Assert.Equal(UsbMode.Wsl, _settings.Current.Mode);
        Assert.True(_log.Contains(LogLevel.Info, "USBIPD Manager is exiting. Devices were returned to Windows."));

        await controller.BackgroundRefreshAsync();
        Assert.Equal(refreshes, _devices.RefreshCalls);
    }

    [Fact]
    public async Task Shutdown_waits_for_the_running_operation()
    {
        var controller = Create();
        await controller.StartAsync();
        var release = new TaskCompletionSource();
        _devices.RefreshHook = () => release.Task;
        var running = controller.RefreshDevicesAsync();
        await WaitUntil(() => _gate.IsBusy);

        var shutdown = controller.ShutdownAsync();
        await Task.Delay(50);
        Assert.False(shutdown.IsCompleted);
        Assert.DoesNotContain("release", _modes.Calls);

        release.SetResult();
        await running;
        await shutdown;
        Assert.Contains("release", _modes.Calls);
    }

    [Fact]
    public async Task Shutdown_without_usbipd_does_not_release()
    {
        _checker.Report = Reports.UsbipdMissing();
        var controller = Create();
        await controller.StartAsync();

        await controller.ShutdownAsync();

        Assert.DoesNotContain("release", _modes.Calls);
        Assert.True(_log.Contains(LogLevel.Info, "USBIPD Manager is exiting."));
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_everything()
    {
        var controller = Create();
        await controller.StartAsync();
        var raised = 0;
        controller.StateChanged += (_, _) => raised++;

        controller.Dispose();
        _devices.RaiseEntriesChanged();
        _settings.Update(s => s.TrayNotifications = false);

        Assert.False(_notifier.HasSubscribers);
        Assert.Equal(0, raised);
    }
}

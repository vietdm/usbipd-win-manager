using UsbipdManager.Core.Models;
using UsbipdManager.Core.Services;
using UsbipdManager.Tests.Services.Fakes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Services;

public sealed class DeviceManagerTests
{
    private const string Pixel = @"VID_18D1\PIXEL8SERIAL";
    private const string Fold = @"VID_18D1\FOLDSERIAL";
    private const string PortA = "1-4";
    private const string PortB = "2-1";

    private readonly FakeUsbipdClient _usbipd = new();
    private readonly FakeWslClient _wsl = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly TestLog _log = new();
    private readonly FakeClock _clock = new();
    private readonly List<TimeSpan> _delays = [];

    // Waits advance the fake clock instead of sleeping.
    private DeviceManager Create()
    {
        var manager = new DeviceManager(_usbipd, _wsl, _settings, _log, () => _clock.Now, d => d.Contains("Keyboard", StringComparison.OrdinalIgnoreCase));
        manager.Delay = (delay, _) =>
        {
            _delays.Add(delay);
            _clock.Advance(delay);
            return Task.CompletedTask;
        };
        return manager;
    }

    private void Remember(string busId, string key, string description = "Pixel 8") =>
        _settings.Update(s => s.ManagedDevices.Add(new ManagedDevice(busId, key, description, _clock.Now)));

    private void SetMode(UsbMode mode, bool autoReattach = true) =>
        _settings.Update(s =>
        {
            s.Mode = mode;
            s.AutoReattach = autoReattach;
        });

    [Theory]
    [InlineData(UsbMode.Windows)]
    [InlineData(UsbMode.Wsl)]
    public async Task Maintainer_scenario_port_and_identity_rules(UsbMode mode)
    {
        SetMode(mode);
        var manager = Create();

        // Port A has Android P, switched ON.
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        await manager.RefreshAsync();
        var p = Assert.Single(manager.Entries);
        Assert.False(p.IsManaged);
        Assert.True((await manager.SetManagedAsync(p, true)).Success);
        Assert.True(Assert.Single(manager.Entries).IsManaged);

        // P unplugged: port A shows P as disconnected, nothing connected on port A is managed.
        _usbipd.Unplug(PortA);
        await manager.RefreshAsync();
        var disconnected = Assert.Single(manager.Entries);
        Assert.Equal(PortA, disconnected.BusId);
        Assert.Equal(Pixel, disconnected.DeviceKey);
        Assert.False(disconnected.IsConnected);
        Assert.True(disconnected.IsManaged);
        Assert.Null(disconnected.State);

        // Android F plugged into port A: OFF and untouched.
        _usbipd.ClearCalls();
        _usbipd.Plug(PortA, Fold, "Pixel Fold");
        await manager.RefreshAsync();
        var f = manager.Entries.Single(e => e.DeviceKey == Fold);
        Assert.True(f.IsConnected);
        Assert.False(f.IsManaged);
        Assert.Equal(UsbDeviceState.NotShared, f.State);
        Assert.Empty(_usbipd.Actions);
        Assert.Contains(manager.Entries, e => e.DeviceKey == Pixel && !e.IsConnected && e.IsManaged);

        // P plugged into port A again: ON automatically (bound; attached when the mode is WSL2).
        _usbipd.Unplug(PortA);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.NotShared);
        _usbipd.ClearCalls();
        await manager.RefreshAsync();
        var back = Assert.Single(manager.Entries);
        Assert.True(back.IsConnected);
        Assert.True(back.IsManaged);
        if (mode == UsbMode.Wsl)
        {
            Assert.Equal(UsbDeviceState.Attached, back.State);
            Assert.Equal(["bind 1-4", "attach 1-4"], _usbipd.Actions);
        }
        else
        {
            Assert.Equal(UsbDeviceState.Shared, back.State);
            Assert.Equal(["bind 1-4"], _usbipd.Actions);
        }

        Assert.True(_log.Contains(LogLevel.Info, "Auto-bind: Pixel 8 is back on port 1-4."));

        // P plugged into port B: OFF, and port A keeps its remembered entry.
        _usbipd.Unplug(PortA);
        _usbipd.Plug(PortB, Pixel, "Pixel 8", UsbDeviceState.NotShared);
        _usbipd.ClearCalls();
        await manager.RefreshAsync();
        var onB = manager.Entries.Single(e => e.BusId == PortB);
        Assert.False(onB.IsManaged);
        Assert.Equal(UsbDeviceState.NotShared, onB.State);
        Assert.Empty(_usbipd.Actions);
        var onA = manager.Entries.Single(e => e.BusId == PortA);
        Assert.False(onA.IsConnected);
        Assert.True(onA.IsManaged);
    }

    [Fact]
    public async Task Managed_device_back_still_shared_is_only_attached_in_wsl_mode()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _settings.Update(s => s.WslDistribution = "Ubuntu-24.04");
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Equal(["attach 1-4"], _usbipd.Actions);
        Assert.Equal(["Ubuntu-24.04"], _wsl.EnsureRunningCalls);
        Assert.Equal(UsbDeviceState.Attached, Assert.Single(manager.Entries).State);
    }

    [Fact]
    public async Task Auto_reattach_off_binds_but_does_not_attach()
    {
        SetMode(UsbMode.Wsl, autoReattach: false);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Equal(["bind 1-4"], _usbipd.Actions);
        Assert.Empty(_wsl.EnsureRunningCalls);
    }

    [Fact]
    public async Task Unmanaged_devices_are_never_touched()
    {
        SetMode(UsbMode.Wsl);
        _usbipd.Plug("1-1", @"VID_046D\MOUSE", "USB Mouse");
        _usbipd.Plug("1-2", Fold, "Pixel Fold", UsbDeviceState.Shared);
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Empty(_usbipd.Actions);
        Assert.Empty(_wsl.EnsureRunningCalls);
        Assert.All(manager.Entries, e => Assert.False(e.IsManaged));
    }

    [Fact]
    public async Task Entries_are_sorted_naturally_with_disconnected_last()
    {
        Remember("1-3", @"VID_1234\GONE", "Gone device");
        _usbipd.Plug("1-10", "K10", "Ten");
        _usbipd.Plug("2-1", "K21", "Two-one");
        _usbipd.Plug("1-2", "K2", "Two");
        _usbipd.Plug("1-1", "K1", "One");
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Equal(["1-1", "1-2", "1-10", "2-1", "1-3"], manager.Entries.Select(e => e.BusId));
        Assert.False(manager.Entries[^1].IsConnected);
    }

    [Fact]
    public async Task Bound_but_unplugged_unmanaged_devices_are_hidden()
    {
        _usbipd.Plug(PortA, Fold, "Pixel Fold", UsbDeviceState.Shared);
        _usbipd.Unplug(PortA);
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Single(_usbipd.Devices);
        Assert.Empty(manager.Entries);
    }

    [Fact]
    public async Task Input_like_devices_are_flagged()
    {
        _usbipd.Plug("1-1", @"VID_046D\KB", "USB Keyboard");
        _usbipd.Plug("1-2", Pixel, "Pixel 8");
        var manager = Create();

        await manager.RefreshAsync();

        Assert.True(manager.Entries.Single(e => e.BusId == "1-1").IsInputLike);
        Assert.False(manager.Entries.Single(e => e.BusId == "1-2").IsInputLike);
    }

    [Fact]
    public async Task Turning_on_in_windows_mode_binds_and_remembers_port_and_identity()
    {
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], true);

        Assert.True(result.Success);
        Assert.Equal(["bind 1-4"], _usbipd.Actions);
        Assert.Empty(_wsl.EnsureRunningCalls);
        var managed = Assert.Single(_settings.Current.ManagedDevices);
        Assert.Equal((PortA, Pixel, "Pixel 8", _clock.Now), (managed.BusId, managed.DeviceKey, managed.Description, managed.AddedAt));
        var entry = Assert.Single(manager.Entries);
        Assert.True(entry.IsManaged);
        Assert.Equal(UsbDeviceState.Shared, entry.State);
    }

    [Fact]
    public async Task Turning_on_in_wsl_mode_ensures_wsl_and_attaches()
    {
        SetMode(UsbMode.Wsl);
        _settings.Update(s => s.WslDistribution = "Debian");
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], true);

        Assert.True(result.Success);
        Assert.Equal(["bind 1-4", "attach 1-4"], _usbipd.Actions);
        Assert.Equal(["Debian"], _wsl.EnsureRunningCalls);
        Assert.Equal(UsbDeviceState.Attached, manager.Entries[0].State);
    }

    [Fact]
    public async Task Turning_on_a_disconnected_entry_fails()
    {
        Remember(PortA, Pixel);
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], true);

        Assert.False(result.Success);
        Assert.Empty(_usbipd.Actions);
    }

    [Fact]
    public async Task Turning_on_fails_when_another_device_took_the_port()
    {
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        var manager = Create();
        await manager.RefreshAsync();
        var stale = manager.Entries[0];
        _usbipd.Plug(PortA, Fold, "Pixel Fold");

        var result = await manager.SetManagedAsync(stale, true);

        Assert.False(result.Success);
        Assert.Empty(_usbipd.Actions);
        Assert.Empty(_settings.Current.ManagedDevices);
        Assert.Equal(Fold, Assert.Single(manager.Entries).DeviceKey);
    }

    [Fact]
    public async Task Turning_on_fails_and_is_not_remembered_when_bind_fails()
    {
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        _usbipd.FailBind.Add(PortA);
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], true);

        Assert.False(result.Success);
        Assert.Empty(_settings.Current.ManagedDevices);
        Assert.True(_log.Contains(LogLevel.Error, "Could not share Pixel 8"));
    }

    [Fact]
    public async Task Several_devices_can_be_remembered_for_the_same_port()
    {
        var manager = Create();
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        await manager.RefreshAsync();
        await manager.SetManagedAsync(manager.Entries[0], true);
        _usbipd.Unplug(PortA);
        _usbipd.Plug(PortA, Fold, "Pixel Fold");
        await manager.RefreshAsync();

        await manager.SetManagedAsync(manager.Entries.Single(e => e.DeviceKey == Fold), true);

        Assert.Equal(2, _settings.Current.ManagedDevices.Count(m => m.BusId == PortA));
        Assert.True(manager.Entries.Single(e => e.DeviceKey == Fold).IsManaged);
        Assert.False(manager.Entries.Single(e => e.DeviceKey == Pixel).IsConnected);
    }

    [Fact]
    public async Task Turning_on_twice_does_not_duplicate_the_remembered_entry()
    {
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        var manager = Create();
        await manager.RefreshAsync();

        await manager.SetManagedAsync(manager.Entries[0], true);
        await manager.SetManagedAsync(manager.Entries[0], true);

        Assert.Single(_settings.Current.ManagedDevices);
    }

    [Fact]
    public async Task Turning_off_an_attached_device_detaches_unbinds_and_forgets()
    {
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Attached);
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], false);

        Assert.True(result.Success);
        Assert.Equal(["detach 1-4", "unbind 1-4"], _usbipd.Actions);
        Assert.Empty(_settings.Current.ManagedDevices);
        var entry = Assert.Single(manager.Entries);
        Assert.False(entry.IsManaged);
        Assert.Equal(UsbDeviceState.NotShared, entry.State);
    }

    [Fact]
    public async Task Turning_off_waits_for_the_re_enumeration_after_detach_before_unbinding()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Attached);
        _usbipd.MissingReadsAfterDetach = 3;
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], false);

        Assert.True(result.Success);
        Assert.Equal(["detach 1-4", "unbind 1-4"], _usbipd.Actions);
        Assert.Empty(_settings.Current.ManagedDevices);
        Assert.Equal(UsbDeviceState.NotShared, Assert.Single(manager.Entries).State);
        Assert.False(_log.Contains(LogLevel.Error, "Could not stop sharing"));
    }

    [Fact]
    public async Task Turning_off_stays_off_when_the_device_does_not_come_back_after_detach()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Attached);
        _usbipd.MissingReadsAfterDetach = 1000;
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], false);

        Assert.True(result.Success);
        Assert.Empty(_settings.Current.ManagedDevices);
        Assert.DoesNotContain(_usbipd.Actions, a => a.StartsWith("attach", StringComparison.Ordinal));
        Assert.True(_log.Contains(LogLevel.Warning, "is OFF and back in Windows, but it is still shared"));
        Assert.True(_delays.Sum(d => d.TotalSeconds) >= DeviceManager.ReenumerationTimeout.TotalSeconds);
    }

    [Fact]
    public async Task Auto_attach_waits_until_a_changed_device_has_settled()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Attached);
        var manager = Create();
        await manager.RefreshAsync();
        Assert.Empty(_delays);

        // Something detached it (WSL restart, device reset): it is re-attached only after the settle time.
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _usbipd.ClearCalls();
        var start = _clock.Now;
        await manager.RefreshAsync();

        Assert.Equal(["attach 1-4"], _usbipd.Actions);
        Assert.Equal(DeviceManager.SettleTime, _clock.Now - start);
        Assert.Equal(UsbDeviceState.Attached, manager.Entries[0].State);
    }

    [Fact]
    public async Task Devices_present_at_the_first_read_do_not_wait_to_settle()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Empty(_delays);
        Assert.Equal(["attach 1-4"], _usbipd.Actions);
    }

    [Fact]
    public async Task A_hanging_auto_attach_gives_up_after_the_auto_attach_timeout()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _usbipd.AttachDelay = TimeSpan.FromSeconds(30);
        var manager = Create();
        manager.AutoAttachTimeout = TimeSpan.FromMilliseconds(50);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await manager.RefreshAsync();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.True(_log.Contains(LogLevel.Error, "did not finish within"));
        Assert.Equal(UsbDeviceState.Shared, manager.Entries[0].State);
    }

    [Fact]
    public async Task Turning_off_keeps_the_device_managed_when_unbind_fails()
    {
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _usbipd.FailUnbind.Add(PortA);
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], false);

        Assert.False(result.Success);
        Assert.Single(_settings.Current.ManagedDevices);
        Assert.True(manager.Entries[0].IsManaged);
    }

    [Fact]
    public async Task Turning_off_a_disconnected_entry_only_forgets_it()
    {
        Remember(PortA, Pixel);
        var manager = Create();
        await manager.RefreshAsync();

        var result = await manager.SetManagedAsync(manager.Entries[0], false);

        Assert.True(result.Success);
        Assert.Empty(_usbipd.Actions);
        Assert.Empty(_settings.Current.ManagedDevices);
        Assert.Empty(manager.Entries);
    }

    [Fact]
    public async Task Forget_removes_only_the_matching_entry_and_raises_EntriesChanged()
    {
        Remember(PortA, Pixel);
        Remember(PortA, Fold, "Pixel Fold");
        var manager = Create();
        await manager.RefreshAsync();
        var raised = 0;
        manager.EntriesChanged += (_, _) => raised++;

        manager.Forget(manager.Entries.Single(e => e.DeviceKey == Pixel));

        Assert.Equal(Fold, Assert.Single(_settings.Current.ManagedDevices).DeviceKey);
        Assert.Equal(Fold, Assert.Single(manager.Entries).DeviceKey);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task EntriesChanged_is_raised_only_when_the_list_changes()
    {
        var manager = Create();
        var raised = 0;
        manager.EntriesChanged += (_, _) => raised++;

        await manager.RefreshAsync();
        Assert.Equal(0, raised);

        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        await manager.RefreshAsync();
        await manager.RefreshAsync();
        Assert.Equal(1, raised);

        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        await manager.RefreshAsync();
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Failed_auto_bind_is_not_retried_for_30_seconds()
    {
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        _usbipd.FailBind.Add(PortA);
        var manager = Create();

        await manager.RefreshAsync();
        _clock.Advance(TimeSpan.FromSeconds(10));
        await manager.RefreshAsync();
        await manager.RefreshAsync();
        Assert.Equal(["bind 1-4"], _usbipd.Actions);
        Assert.Single(_log.GetEntries(), e => e.Level == LogLevel.Error);

        _clock.Advance(TimeSpan.FromSeconds(21));
        _usbipd.FailBind.Clear();
        await manager.RefreshAsync();
        Assert.Equal(["bind 1-4", "bind 1-4"], _usbipd.Actions);
        Assert.Equal(UsbDeviceState.Shared, manager.Entries[0].State);
    }

    [Fact]
    public async Task Failed_auto_attach_is_not_retried_for_30_seconds()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _usbipd.FailAttach.Add(PortA);
        var manager = Create();

        await manager.RefreshAsync();
        await manager.RefreshAsync();
        Assert.Equal(["attach 1-4"], _usbipd.Actions);

        _clock.Advance(TimeSpan.FromSeconds(31));
        await manager.RefreshAsync();
        Assert.Equal(["attach 1-4", "attach 1-4"], _usbipd.Actions);
    }

    [Fact]
    public async Task Failed_wsl_start_backs_off_auto_attach()
    {
        SetMode(UsbMode.Wsl);
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8", UsbDeviceState.Shared);
        _wsl.EnsureRunningResult = OperationResult.Fail("no distro");
        var manager = Create();

        await manager.RefreshAsync();
        await manager.RefreshAsync();

        Assert.Single(_wsl.EnsureRunningCalls);
        Assert.Empty(_usbipd.Actions);
        Assert.True(_log.Contains(LogLevel.Error, "Could not start WSL 2 for auto-attach"));
    }

    [Fact]
    public async Task Replugging_a_device_clears_its_back_off()
    {
        Remember(PortA, Pixel);
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        _usbipd.FailBind.Add(PortA);
        var manager = Create();
        await manager.RefreshAsync();

        _usbipd.Unplug(PortA);
        await manager.RefreshAsync();
        _usbipd.Plug(PortA, Pixel, "Pixel 8");
        await manager.RefreshAsync();

        Assert.Equal(["bind 1-4", "bind 1-4"], _usbipd.Actions);
    }

    [Fact]
    public async Task Bind_connected_managed_binds_only_managed_unshared_devices_and_aggregates_failures()
    {
        Remember("1-1", "K1", "One");
        Remember("1-2", "K2", "Two");
        Remember("1-3", "K3", "Three");
        _usbipd.Plug("1-1", "K1", "One");
        _usbipd.Plug("1-2", "K2", "Two");
        _usbipd.Plug("1-3", "K3", "Three", UsbDeviceState.Shared);
        _usbipd.Plug("1-5", "K5", "Unmanaged");
        _usbipd.FailBind.Add("1-2");
        var manager = Create();

        var result = await manager.BindConnectedManagedAsync();

        Assert.False(result.Success);
        Assert.Contains("Two", result.Error);
        Assert.Equal(["bind 1-1", "bind 1-2"], _usbipd.Actions);
        Assert.Equal(UsbDeviceState.Shared, manager.Entries.Single(e => e.BusId == "1-1").State);
    }

    [Fact]
    public async Task Attach_connected_managed_ensures_wsl_then_binds_and_attaches()
    {
        _settings.Update(s => s.WslDistribution = "Ubuntu");
        Remember("1-1", "K1", "One");
        Remember("1-2", "K2", "Two");
        Remember("1-9", "K9", "Gone");
        _usbipd.Plug("1-1", "K1", "One");
        _usbipd.Plug("1-2", "K2", "Two", UsbDeviceState.Shared);
        _usbipd.Plug("1-5", "K5", "Unmanaged", UsbDeviceState.Shared);
        var manager = Create();

        var result = await manager.AttachConnectedManagedAsync();

        Assert.True(result.Success);
        Assert.Equal(["Ubuntu"], _wsl.EnsureRunningCalls);
        Assert.Equal(["bind 1-1", "attach 1-1", "attach 1-2"], _usbipd.Actions);
        Assert.All(manager.Entries.Where(e => e.IsConnected && e.IsManaged), e => Assert.Equal(UsbDeviceState.Attached, e.State));
    }

    [Fact]
    public async Task Attach_connected_managed_aggregates_failures()
    {
        Remember("1-1", "K1", "One");
        Remember("1-2", "K2", "Two");
        _usbipd.Plug("1-1", "K1", "One", UsbDeviceState.Shared);
        _usbipd.Plug("1-2", "K2", "Two", UsbDeviceState.Shared);
        _usbipd.FailAttach.Add("1-1");
        var manager = Create();

        var result = await manager.AttachConnectedManagedAsync();

        Assert.False(result.Success);
        Assert.Contains("One", result.Error);
        Assert.Equal(["attach 1-1", "attach 1-2"], _usbipd.Actions);
        Assert.True(_log.Contains(LogLevel.Error, "Could not attach One (port 1-1) to WSL2"));
        Assert.True(_log.Contains(LogLevel.Success, "Two (port 1-2) is attached to WSL2."));
    }

    [Fact]
    public async Task Attach_connected_managed_fails_without_attaching_when_wsl_cannot_start()
    {
        Remember("1-1", "K1", "One");
        _usbipd.Plug("1-1", "K1", "One", UsbDeviceState.Shared);
        _wsl.EnsureRunningResult = OperationResult.Fail("WSL did not start");
        var manager = Create();

        var result = await manager.AttachConnectedManagedAsync();

        Assert.False(result.Success);
        Assert.Empty(_usbipd.Actions);
    }

    [Fact]
    public async Task Attach_connected_managed_with_nothing_to_attach_does_not_start_wsl()
    {
        _usbipd.Plug("1-1", "K1", "One", UsbDeviceState.Shared);
        var manager = Create();

        var result = await manager.AttachConnectedManagedAsync();

        Assert.True(result.Success);
        Assert.Empty(_wsl.EnsureRunningCalls);
    }

    [Fact]
    public async Task Without_usbipd_remembered_devices_are_shown_disconnected()
    {
        Remember(PortA, Pixel);
        _usbipd.ExecutablePath = null;
        var manager = Create();

        await manager.RefreshAsync();

        Assert.Empty(_usbipd.Calls);
        Assert.False(Assert.Single(manager.Entries).IsConnected);
    }
}

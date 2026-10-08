using UsbipdManager.Core.Models;
using UsbipdManager.Core.Services;
using UsbipdManager.Tests.Services.Fakes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Services;

public sealed class ModeSwitcherTests
{
    private readonly FakeUsbipdClient _usbipd = new();
    private readonly FakeWslClient _wsl = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly TestLog _log = new();
    private readonly DeviceManager _devices;
    private readonly ModeSwitcher _switcher;

    public ModeSwitcherTests()
    {
        _devices = new DeviceManager(_usbipd, _wsl, _settings, _log, () => DateTimeOffset.Now, _ => false);
        _switcher = new ModeSwitcher(_usbipd, _wsl, _devices, _settings, _log);
    }

    private void Remember(string busId, string key) =>
        _settings.Update(s => s.ManagedDevices.Add(new ManagedDevice(busId, key, "Pixel 8", DateTimeOffset.Now)));

    [Fact]
    public async Task Switch_to_windows_detaches_all_stops_keep_alive_and_saves_the_mode()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        _usbipd.Plug("1-4", "K4", "Pixel 8", UsbDeviceState.Attached);

        var result = await _switcher.SwitchToWindowsAsync();

        Assert.True(result.Success);
        Assert.Equal(["detach --all"], _usbipd.Actions);
        Assert.Equal(1, _wsl.StopKeepAliveCalls);
        Assert.Equal(UsbMode.Windows, _settings.Current.Mode);
        Assert.Equal(UsbMode.Windows, _switcher.Mode);
        Assert.True(_log.Contains(LogLevel.Success, "Switched to Windows."));
    }

    [Fact]
    public async Task Switch_to_windows_fails_without_usbipd()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        _usbipd.ExecutablePath = null;

        var result = await _switcher.SwitchToWindowsAsync();

        Assert.False(result.Success);
        Assert.Empty(_usbipd.Calls);
        Assert.Equal(UsbMode.Wsl, _settings.Current.Mode);
    }

    [Fact]
    public async Task Switch_to_wsl_ensures_the_distro_saves_the_mode_and_attaches_managed_devices()
    {
        _settings.Update(s => s.WslDistribution = "Ubuntu-24.04");
        Remember("1-4", "K4");
        _usbipd.Plug("1-4", "K4", "Pixel 8", UsbDeviceState.Shared);
        _usbipd.Plug("1-5", "K5", "Unmanaged", UsbDeviceState.Shared);

        var result = await _switcher.SwitchToWslAsync();

        Assert.True(result.Success);
        Assert.Equal("Ubuntu-24.04", _wsl.EnsureRunningCalls[0]);
        Assert.Equal(["attach 1-4"], _usbipd.Actions);
        Assert.Equal(UsbMode.Wsl, _settings.Current.Mode);
        Assert.True(_log.Contains(LogLevel.Success, "Switched to WSL2."));
        Assert.False(_log.Contains(LogLevel.Warning, "No managed device"));
    }

    [Fact]
    public async Task Switch_to_wsl_without_managed_devices_warns_but_keeps_wsl_mode()
    {
        _usbipd.Plug("1-5", "K5", "Unmanaged", UsbDeviceState.Shared);

        var result = await _switcher.SwitchToWslAsync();

        Assert.True(result.Success);
        Assert.Equal(UsbMode.Wsl, _settings.Current.Mode);
        Assert.Empty(_usbipd.Actions);
        Assert.True(_log.Contains(LogLevel.Warning, "No managed device is connected. Turn on a device's switch to move it to WSL2."));
        Assert.True(_log.Contains(LogLevel.Success, "Switched to WSL2."));
    }

    [Fact]
    public async Task Switch_to_wsl_keeps_the_mode_when_wsl_cannot_start()
    {
        Remember("1-4", "K4");
        _usbipd.Plug("1-4", "K4", "Pixel 8", UsbDeviceState.Shared);
        _wsl.EnsureRunningResult = OperationResult.Fail("The distribution failed to start.");

        var result = await _switcher.SwitchToWslAsync();

        Assert.False(result.Success);
        Assert.Equal(UsbMode.Windows, _settings.Current.Mode);
        Assert.Empty(_usbipd.Actions);
        Assert.True(_log.Contains(LogLevel.Error, "Could not start WSL 2"));
    }

    [Fact]
    public async Task Release_for_exit_detaches_and_stops_keep_alive_but_keeps_the_saved_mode()
    {
        _settings.Update(s => s.Mode = UsbMode.Wsl);
        _usbipd.Plug("1-4", "K4", "Pixel 8", UsbDeviceState.Attached);
        var updatesBefore = _settings.UpdateCount;

        var result = await _switcher.ReleaseForExitAsync();

        Assert.True(result.Success);
        Assert.Equal(["detach --all"], _usbipd.Actions);
        Assert.Equal(1, _wsl.StopKeepAliveCalls);
        Assert.Equal(UsbMode.Wsl, _settings.Current.Mode);
        Assert.Equal(updatesBefore, _settings.UpdateCount);
    }
}

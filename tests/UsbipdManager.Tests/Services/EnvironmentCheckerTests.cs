using UsbipdManager.Core.Models;
using UsbipdManager.Core.Services;
using UsbipdManager.Tests.Services.Fakes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Services;

public sealed class EnvironmentCheckerTests
{
    private readonly FakeUsbipdClient _usbipd = new();
    private readonly FakeWslClient _wsl = new();
    private readonly FakeUsbipdServiceController _service = new();
    private readonly TestLog _log = new();

    private EnvironmentChecker Create(bool elevated = true) => new(_usbipd, _wsl, _service, _log, () => elevated);

    private List<(LogLevel, string)> Lines() => _log.GetEntries().Select(e => (e.Level, e.Message)).ToList();

    [Fact]
    public async Task Supported_machine_logs_every_check_in_order()
    {
        _wsl.Distros.Clear();
        _wsl.Distros.Add(new WslDistro("Ubuntu", true, 2, true));
        _wsl.Distros.Add(new WslDistro("Legacy", false, 1, false));
        _usbipd.Plug("1-10", "K10", "USB Serial", UsbDeviceState.Shared, "0403:6001");
        _usbipd.Plug("1-4", @"VID_18D1\SERIAL", "Pixel 8", UsbDeviceState.Attached, "18d1:4ee7");
        _usbipd.Plug("1-7", "K7", "Gone", UsbDeviceState.Shared);
        _usbipd.Unplug("1-7");

        var report = await Create().CheckAsync();

        Assert.True(report.IsSupported);
        Assert.True(report.CanSwitch);
        Assert.Equal("5.3.0", report.UsbipdVersion);
        Assert.Equal(3, report.Devices.Count);
        Assert.Equal(
            [
                (LogLevel.Info, "Checking environment..."),
                (LogLevel.Success, "Running as administrator."),
                (LogLevel.Success, "usbipd-win 5.3.0 is installed."),
                (LogLevel.Success, "usbipd service is running."),
                (LogLevel.Success, "WSL 2 distribution: Ubuntu (running, default)"),
                (LogLevel.Info, "WSL 1 distribution: Legacy (stopped) is not usable. Convert it with: wsl --set-version Legacy 2"),
                (LogLevel.Info, "Found 2 USB device(s)."),
                (LogLevel.Info, "1-4  18d1:4ee7  Pixel 8  [Attached]"),
                (LogLevel.Info, "1-10  0403:6001  USB Serial  [Shared]"),
            ],
            Lines());
    }

    [Fact]
    public async Task Not_elevated_is_an_error()
    {
        var report = await Create(elevated: false).CheckAsync();

        Assert.False(report.IsElevated);
        Assert.True(_log.Contains(LogLevel.Error, "Not running as administrator. Restart USBIPD Manager as administrator."));
    }

    [Fact]
    public async Task No_wsl_makes_the_machine_unsupported()
    {
        _wsl.Installed = false;

        var report = await Create().CheckAsync();

        Assert.False(report.WslInstalled);
        Assert.False(report.IsSupported);
        Assert.Empty(report.Distros);
        Assert.True(_log.Contains(LogLevel.Error, "WSL is not installed on this machine. USBIPD Manager is not supported."));
    }

    [Fact]
    public async Task Only_wsl1_distros_make_the_machine_unsupported()
    {
        _wsl.Distros.Clear();
        _wsl.Distros.Add(new WslDistro("Legacy", false, 1, true));

        var report = await Create().CheckAsync();

        Assert.True(report.WslInstalled);
        Assert.False(report.IsSupported);
        Assert.True(_log.Contains(LogLevel.Error, "No WSL 2 distribution found. USBIPD Manager is not supported."));
    }

    [Fact]
    public async Task Missing_usbipd_skips_the_device_list()
    {
        _usbipd.ExecutablePath = null;
        _service.Info = UsbipdServiceInfo.NotFound;

        var report = await Create().CheckAsync();

        Assert.False(report.UsbipdInstalled);
        Assert.False(report.CanSwitch);
        Assert.True(report.IsSupported);
        Assert.Empty(_usbipd.Calls);
        Assert.True(_log.Contains(LogLevel.Error, "usbipd-win is not installed. Press Init to install it."));
        Assert.DoesNotContain(_log.GetEntries(), e => e.Message.Contains("usbipd service"));
    }

    [Fact]
    public async Task Stopped_service_is_a_warning()
    {
        _service.Info = new UsbipdServiceInfo(true, false, ServiceStartupType.Manual);

        var report = await Create().CheckAsync();

        Assert.False(report.CanSwitch);
        Assert.True(_log.Contains(LogLevel.Warning, "usbipd service is stopped. Press Init to start it."));
    }

    [Fact]
    public async Task Disabled_service_is_a_warning()
    {
        _service.Info = new UsbipdServiceInfo(true, false, ServiceStartupType.Disabled);

        await Create().CheckAsync();

        Assert.True(_log.Contains(LogLevel.Warning, "usbipd service is disabled"));
    }

    [Fact]
    public async Task Missing_service_with_usbipd_installed_is_an_error()
    {
        _service.Info = UsbipdServiceInfo.NotFound;

        await Create().CheckAsync();

        Assert.True(_log.Contains(LogLevel.Error, "usbipd service was not found"));
    }

    [Fact]
    public async Task A_failing_check_is_logged_and_counts_as_missing()
    {
        _wsl.InstalledException = new InvalidOperationException("wsl.exe crashed");
        _usbipd.GetDevicesException = new InvalidOperationException("usbipd state failed");

        var report = await Create().CheckAsync();

        Assert.False(report.WslInstalled);
        Assert.Empty(report.Devices);
        Assert.True(report.UsbipdInstalled);
        Assert.True(_log.Contains(LogLevel.Error, "Could not check WSL: wsl.exe crashed"));
        Assert.True(_log.Contains(LogLevel.Error, "Could not check USB devices: usbipd state failed"));
    }
}

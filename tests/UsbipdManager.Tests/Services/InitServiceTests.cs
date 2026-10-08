using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Services;
using UsbipdManager.Tests.Services.Fakes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Services;

public sealed class InitServiceTests
{
    private readonly FakeUsbipdClient _usbipd = new();
    private readonly FakeWslClient _wsl = new();
    private readonly FakeUsbipdServiceController _service = new();
    private readonly FakeUsbipdInstaller _installer = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly TestLog _log = new();

    private InitService Create(out DeviceManager devices)
    {
        devices = new DeviceManager(_usbipd, _wsl, _settings, _log, () => DateTimeOffset.Now, _ => false);
        var checker = new EnvironmentChecker(_usbipd, _wsl, _service, _log, () => true);
        return new InitService(checker, _installer, _service, devices, _log);
    }

    [Fact]
    public async Task Installs_usbipd_starts_the_service_and_binds_managed_devices()
    {
        _usbipd.ExecutablePath = null;
        _service.Info = UsbipdServiceInfo.NotFound;
        _installer.OnInstalled = () =>
        {
            _usbipd.ExecutablePath = @"C:\Program Files\usbipd-win\usbipd.exe";
            _service.Info = new UsbipdServiceInfo(true, false, ServiceStartupType.Automatic);
        };
        _settings.Update(s => s.ManagedDevices.Add(new ManagedDevice("1-4", "K4", "Pixel 8", DateTimeOffset.Now)));
        _usbipd.Plug("1-4", "K4", "Pixel 8");
        _usbipd.Plug("1-5", "K5", "Other");

        var report = await Create(out _).RunAsync();

        Assert.Equal(1, _installer.InstallCalls);
        Assert.Equal(1, _service.StartCalls);
        Assert.Equal(0, _service.SetAutomaticCalls);
        Assert.Equal(["bind 1-4"], _usbipd.Actions);
        Assert.True(report.CanSwitch);
        Assert.True(_log.Contains(LogLevel.Success, "Init completed."));
    }

    [Fact]
    public async Task Disabled_service_is_set_to_automatic_and_started()
    {
        _service.Info = new UsbipdServiceInfo(true, false, ServiceStartupType.Disabled);

        var report = await Create(out _).RunAsync();

        Assert.Equal(0, _installer.InstallCalls);
        Assert.Equal(1, _service.SetAutomaticCalls);
        Assert.Equal(1, _service.StartCalls);
        Assert.True(report.UsbipdService.IsRunning);
        Assert.True(_log.Contains(LogLevel.Success, "Init completed."));
    }

    [Fact]
    public async Task Running_service_is_left_alone()
    {
        await Create(out _).RunAsync();

        Assert.Equal(0, _service.StartCalls);
        Assert.Equal(0, _service.SetAutomaticCalls);
    }

    [Fact]
    public async Task Unsupported_machine_stops_before_fixing_anything()
    {
        _wsl.Installed = false;
        _usbipd.ExecutablePath = null;

        var report = await Create(out _).RunAsync();

        Assert.False(report.IsSupported);
        Assert.Equal(0, _installer.InstallCalls);
        Assert.Equal(0, _service.StartCalls);
        Assert.True(_log.Contains(LogLevel.Error, "Init stopped: WSL 2 is required."));
        Assert.DoesNotContain(_log.GetEntries(), e => e.Message.StartsWith("Init completed"));
    }

    [Fact]
    public async Task Missing_winget_logs_the_release_page()
    {
        _usbipd.ExecutablePath = null;
        _service.Info = UsbipdServiceInfo.NotFound;
        _installer.WingetAvailable = false;

        var report = await Create(out _).RunAsync();

        Assert.False(report.UsbipdInstalled);
        Assert.Equal(0, _installer.InstallCalls);
        Assert.True(_log.Contains(LogLevel.Error, IUsbipdInstaller.ReleasePageUrl));
        Assert.True(_log.Contains(LogLevel.Warning, "usbipd-win is not installed"));
    }

    [Fact]
    public async Task Failed_service_start_is_reported_as_not_ready()
    {
        _service.Info = new UsbipdServiceInfo(true, false, ServiceStartupType.Manual);
        _service.StartResult = OperationResult.Fail("Access is denied.");

        var report = await Create(out _).RunAsync();

        Assert.False(report.CanSwitch);
        Assert.Empty(_usbipd.Actions);
        Assert.True(_log.Contains(LogLevel.Error, "Could not start the usbipd service: Access is denied."));
        Assert.True(_log.Contains(LogLevel.Warning, "usbipd service is not running"));
    }
}

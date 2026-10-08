using System.ComponentModel;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Usbipd;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Usbipd;

public sealed class UsbipdClientTests
{
    private const string ExePath = @"C:\Program Files\usbipd-win\usbipd.exe";

    private readonly FakeProcessRunner _runner = new();
    private readonly TestLog _log = new();

    private UsbipdClient CreateClient(string? exePath = ExePath) => new(_runner, _log, () => exePath);

    [Fact]
    public async Task Operations_UseTheExpectedCommandLines()
    {
        var client = CreateClient();

        await client.BindAsync("1-4");
        await client.UnbindAsync("1-4");
        await client.AttachToWslAsync("1-4");
        await client.DetachAsync("1-4");
        await client.DetachAllAsync();

        Assert.Equal(
            [
                "usbipd.exe bind --busid 1-4",
                "usbipd.exe unbind --busid 1-4",
                "usbipd.exe attach --wsl --busid 1-4",
                "usbipd.exe detach --busid 1-4",
                "usbipd.exe detach --all",
            ],
            _runner.Calls.Select(c => c.CommandLine));
        Assert.All(_runner.Calls, c => Assert.Equal(ExePath, c.FileName));
        Assert.All(_runner.Calls, c => Assert.True(c.Options?.LogCommand ?? true));
    }

    [Fact]
    public async Task Attach_UsesLongerTimeout()
    {
        var client = CreateClient();

        await client.AttachToWslAsync("1-4");
        await client.BindAsync("1-4");

        Assert.Equal(TimeSpan.FromSeconds(60), _runner.Calls[0].Options?.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), _runner.Calls[1].Options?.Timeout);
    }

    [Fact]
    public async Task Operation_Failure_ExtractsUsbipdErrorLine()
    {
        _runner.Handler = _ => new ProcessResult(
            1,
            string.Empty,
            "usbipd: info: Using WSL distribution 'Ubuntu-26.04' to attach.\r\nusbipd: error: Device is not shared; run 'usbipd bind --busid 1-4' as administrator first.\r\n");
        var client = CreateClient();

        var result = await client.AttachToWslAsync("1-4");

        Assert.False(result.Success);
        Assert.Equal("Device is not shared; run 'usbipd bind --busid 1-4' as administrator first.", result.Error);
    }

    [Fact]
    public async Task Operation_Failure_ErrorLineOnStdout()
    {
        _runner.Handler = _ => new ProcessResult(1, "usbipd: error: There is no device with busid '9-9'.\n", string.Empty);

        var result = await CreateClient().BindAsync("9-9");

        Assert.Equal("There is no device with busid '9-9'.", result.Error);
    }

    [Fact]
    public async Task Operation_Failure_WithoutErrorLine_UsesLastLineOrExitCode()
    {
        _runner.Handler = _ => new ProcessResult(5, string.Empty, "first\nsomething went wrong\n");
        var withText = await CreateClient().BindAsync("1-4");

        _runner.Handler = _ => new ProcessResult(5, string.Empty, string.Empty);
        var empty = await CreateClient().BindAsync("1-4");

        Assert.Equal("something went wrong", withText.Error);
        Assert.Equal("usbipd exited with code 5.", empty.Error);
    }

    [Fact]
    public async Task Attach_AlreadyAttached_IsSuccess()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "usbipd: error: Device with busid '1-4' is already attached to a client.\n");

        var result = await CreateClient().AttachToWslAsync("1-4");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Bind_AlreadyShared_IsSuccess()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "usbipd: error: Device is already shared.\n");

        var result = await CreateClient().BindAsync("1-4");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Operation_Timeout_Fails()
    {
        _runner.Handler = _ => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);

        var result = await CreateClient().AttachToWslAsync("1-4");

        Assert.False(result.Success);
        Assert.Contains("60 seconds", result.Error);
    }

    [Fact]
    public async Task Operation_NotInstalled_FailsWithoutRunning()
    {
        var client = CreateClient(exePath: null);

        var result = await client.BindAsync("1-4");

        Assert.Equal(OperationResult.Fail("usbipd-win is not installed."), result);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Operation_Win32Exception_Fails()
    {
        _runner.Handler = _ => throw new Win32Exception(2, "The system cannot find the file specified.");

        var result = await CreateClient().DetachAllAsync();

        Assert.False(result.Success);
        Assert.Contains("The system cannot find the file specified.", result.Error);
    }

    [Fact]
    public async Task GetDevices_ParsesStateOutput_WithoutLoggingTheCommand()
    {
        _runner.Handler = _ => new ProcessResult(
            0,
            """{"Devices":[{"BusId":"1-4","ClientIPAddress":null,"Description":"Pixel 8","InstanceId":"USB\\VID_18D1&PID_4EE7\\35201FDH2000Q5","PersistedGuid":"x"}]}""",
            string.Empty);

        var devices = await CreateClient().GetDevicesAsync();

        var device = Assert.Single(devices);
        Assert.Equal(UsbDeviceState.Shared, device.State);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("usbipd.exe state", call.CommandLine);
        Assert.False(call.Options?.LogCommand);
    }

    [Fact]
    public async Task GetDevices_NotInstalled_ReturnsEmpty()
    {
        var devices = await CreateClient(exePath: null).GetDevicesAsync();

        Assert.Empty(devices);
        Assert.Empty(_runner.Calls);
        Assert.Empty(_log.GetEntries());
    }

    [Fact]
    public async Task GetDevices_Failure_ReturnsEmptyAndWarns()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "usbipd: error: The service is not running.\n");

        var devices = await CreateClient().GetDevicesAsync();

        Assert.Empty(devices);
        Assert.True(_log.Contains(LogLevel.Warning, "The service is not running."));
    }

    [Fact]
    public async Task GetDevices_InvalidJson_ReturnsEmptyAndWarns()
    {
        _runner.Handler = _ => new ProcessResult(0, "garbage", string.Empty);

        var devices = await CreateClient().GetDevicesAsync();

        Assert.Empty(devices);
        Assert.True(_log.Contains(LogLevel.Warning, "device list"));
    }

    [Fact]
    public async Task GetVersion_ReturnsFirstNonEmptyLine()
    {
        _runner.Handler = _ => new ProcessResult(0, "\r\n4.3.0+12.Branch.master.Sha.abc\r\n", string.Empty);

        var version = await CreateClient().GetVersionAsync();

        Assert.Equal("4.3.0+12.Branch.master.Sha.abc", version);
        Assert.Equal("usbipd.exe --version", Assert.Single(_runner.Calls).CommandLine);
    }

    [Fact]
    public async Task GetVersion_NotInstalledOrFailing_ReturnsNull()
    {
        Assert.Null(await CreateClient(exePath: null).GetVersionAsync());

        _runner.Handler = _ => new ProcessResult(1, string.Empty, "boom");
        Assert.Null(await CreateClient().GetVersionAsync());
    }

    [Fact]
    public void ResolveExecutablePath_UsesTheSeam()
    {
        Assert.Equal(ExePath, CreateClient().ResolveExecutablePath());
        Assert.Null(CreateClient(exePath: null).ResolveExecutablePath());
    }

    [Fact]
    public void FindInstalledExecutable_DoesNotThrow()
    {
        // usbipd-win may or may not be installed on the test machine; only the lookup itself is exercised.
        var path = UsbipdClient.FindInstalledExecutable();

        Assert.True(path is null || File.Exists(path));
    }
}

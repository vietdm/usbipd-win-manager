using UsbipdManager.Core.Models;
using UsbipdManager.Core.Usbipd;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Usbipd;

public sealed class UsbipdInstallerTests
{
    private const string WingetPath = @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\winget.exe";

    private readonly FakeProcessRunner _runner = new();
    private readonly TestLog _log = new();

    private UsbipdInstaller CreateInstaller(string? wingetPath = WingetPath) => new(_runner, _log, () => wingetPath);

    [Fact]
    public async Task Install_RunsWingetWithSilentArguments()
    {
        var result = await CreateInstaller().InstallAsync();

        Assert.True(result.Success);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(WingetPath, call.FileName);
        Assert.Equal(
            "winget.exe install --id dorssel.usbipd-win -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            call.CommandLine);
        Assert.Equal(TimeSpan.FromMinutes(10), call.Options?.Timeout);
        Assert.True(_log.Contains(LogLevel.Info, "This can take a few minutes"));
    }

    [Fact]
    public async Task Install_AlreadyInstalledExitCode_IsSuccess()
    {
        _runner.Handler = _ => new ProcessResult(-1978335189, "Found an existing package already installed.", string.Empty);

        var result = await CreateInstaller().InstallAsync();

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Install_Failure_UsesLastMeaningfulLine()
    {
        const string output = "   - \r   \\ \r   | \r  ██████████▒▒▒▒  3.00 MB / 9.00 MB\r\nInstaller failed with exit code: 1603\r\n  \r\n";
        _runner.Handler = _ => new ProcessResult(unchecked((int)0x8A150006), output, string.Empty);

        var result = await CreateInstaller().InstallAsync();

        Assert.False(result.Success);
        Assert.Equal("winget failed: Installer failed with exit code: 1603", result.Error);
    }

    [Fact]
    public async Task Install_FailureWithoutOutput_UsesExitCode()
    {
        _runner.Handler = _ => new ProcessResult(unchecked((int)0x8A150006), string.Empty, string.Empty);

        var result = await CreateInstaller().InstallAsync();

        Assert.Equal("winget failed with exit code 0x8A150006.", result.Error);
    }

    [Fact]
    public async Task Install_Timeout_Fails()
    {
        _runner.Handler = _ => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);

        var result = await CreateInstaller().InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("10 minutes", result.Error);
    }

    [Fact]
    public async Task Install_WithoutWinget_FailsWithReleasePage()
    {
        var installer = CreateInstaller(wingetPath: null);

        var result = await installer.InstallAsync();

        Assert.False(installer.IsWingetAvailable());
        Assert.False(result.Success);
        Assert.Contains("https://github.com/dorssel/usbipd-win/releases/latest", result.Error);
        Assert.Empty(_runner.Calls);
    }
}

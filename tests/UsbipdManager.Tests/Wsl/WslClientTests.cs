using System.ComponentModel;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Wsl;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Wsl;

public sealed class WslClientTests
{
    private const string WslPath = @"C:\Windows\System32\wsl.exe";

    private const string StoppedTable =
        "  NAME            STATE           VERSION\r\n" +
        "* Ubuntu-26.04    Stopped         2\r\n" +
        "  Legacy          Stopped         1\r\n";

    private const string RunningTable =
        "  NAME            STATE           VERSION\r\n" +
        "* Ubuntu-26.04    Running         2\r\n" +
        "  Legacy          Stopped         1\r\n";

    private readonly FakeProcessRunner _runner = new();
    private readonly TestLog _log = new();

    private WslClient CreateClient(bool wslExists = true) => new(_runner, _log, WslPath)
    {
        FileExists = _ => wslExists,
        StartPollInterval = TimeSpan.FromMilliseconds(1),
        StartTimeout = TimeSpan.FromMilliseconds(300),
    };

    /// <summary>The distro reports Running once a keep-alive process has been started.</summary>
    private void SimulateDistroStartsWithKeepAlive() =>
        _runner.Handler = _ => new ProcessResult(0, _runner.StartedProcesses.Count > 0 ? RunningTable : StoppedTable, string.Empty);

    [Fact]
    public async Task IsInstalled_RequiresExeAndSuccessfulStatus()
    {
        Assert.False(await CreateClient(wslExists: false).IsInstalledAsync());
        Assert.Empty(_runner.Calls);

        _runner.Handler = _ => new ProcessResult(0, "Default Version: 2", string.Empty);
        Assert.True(await CreateClient().IsInstalledAsync());

        _runner.Handler = _ => new ProcessResult(-1, "The Windows Subsystem for Linux is not installed.", string.Empty);
        Assert.False(await CreateClient().IsInstalledAsync());

        _runner.Handler = _ => throw new Win32Exception(2);
        Assert.False(await CreateClient().IsInstalledAsync());

        Assert.All(_runner.Calls, c => Assert.Equal("wsl.exe --status", c.CommandLine));
    }

    [Fact]
    public async Task Queries_SetWslUtf8()
    {
        _runner.Handler = _ => new ProcessResult(0, RunningTable, string.Empty);

        await CreateClient().GetDistrosAsync();

        var call = Assert.Single(_runner.Calls);
        Assert.Equal("wsl.exe -l -v", call.CommandLine);
        Assert.Equal("1", call.Options?.EnvironmentVariables?["WSL_UTF8"]);
        Assert.False(call.Options?.LogCommand);
    }

    [Fact]
    public async Task GetDistros_NoDistributionAndNonZeroExit_ReturnsEmpty()
    {
        _runner.Handler = _ => new ProcessResult(-1, "Windows Subsystem for Linux has no installed distributions.", string.Empty);

        Assert.Empty(await CreateClient().GetDistrosAsync());
    }

    [Fact]
    public async Task EnsureRunning_StartsKeepAliveAndWaitsForRunning()
    {
        SimulateDistroStartsWithKeepAlive();
        var client = CreateClient();

        var result = await client.EnsureRunningAsync(null);

        Assert.True(result.Success, result.Error);
        Assert.True(client.IsKeepAliveRunning);
        Assert.Contains(_runner.Calls, c => c.CommandLine == "wsl.exe -d Ubuntu-26.04 --exec sleep infinity");
        var keepAliveCall = _runner.Calls.Single(c => c.Arguments.Contains("sleep"));
        Assert.Equal("1", keepAliveCall.Options?.EnvironmentVariables?["WSL_UTF8"]);
        Assert.True(_log.Contains(LogLevel.Info, "Starting WSL distribution Ubuntu-26.04..."));
        Assert.True(_log.Contains(LogLevel.Success, "WSL distribution Ubuntu-26.04 is running."));
    }

    [Fact]
    public async Task EnsureRunning_WhenKeepAliveIsAlive_DoesNothing()
    {
        SimulateDistroStartsWithKeepAlive();
        var client = CreateClient();
        await client.EnsureRunningAsync(null);
        var callCount = _runner.Calls.Count;

        var result = await client.EnsureRunningAsync(null);

        Assert.True(result.Success);
        Assert.Equal(callCount, _runner.Calls.Count);
        Assert.Single(_runner.StartedProcesses);
    }

    [Fact]
    public async Task EnsureRunning_AfterKeepAliveExited_StartsANewOne()
    {
        SimulateDistroStartsWithKeepAlive();
        var client = CreateClient();
        await client.EnsureRunningAsync(null);

        _runner.StartedProcesses[0].SimulateExit();
        Assert.False(client.IsKeepAliveRunning);
        Assert.True(_log.Contains(LogLevel.Warning, "keep-alive"));

        var result = await client.EnsureRunningAsync(null);

        Assert.True(result.Success);
        Assert.Equal(2, _runner.StartedProcesses.Count);
        Assert.True(client.IsKeepAliveRunning);
    }

    [Fact]
    public async Task EnsureRunning_NamedDistro()
    {
        _runner.Handler = _ => new ProcessResult(0, "  NAME STATE VERSION\n* Ubuntu-26.04 Running 2\n  Debian Running 2\n", string.Empty);
        var client = CreateClient();

        var result = await client.EnsureRunningAsync("debian");

        Assert.True(result.Success);
        Assert.Contains(_runner.Calls, c => c.CommandLine == "wsl.exe -d Debian --exec sleep infinity");
        Assert.False(_log.Contains(LogLevel.Info, "Starting WSL distribution"));
    }

    [Fact]
    public async Task EnsureRunning_DifferentDistro_ReplacesKeepAlive()
    {
        _runner.Handler = _ => new ProcessResult(0, "  NAME STATE VERSION\n* Ubuntu-26.04 Running 2\n  Debian Running 2\n", string.Empty);
        var client = CreateClient();
        await client.EnsureRunningAsync(null);

        await client.EnsureRunningAsync("Debian");

        Assert.Equal(2, _runner.StartedProcesses.Count);
        Assert.True(_runner.StartedProcesses[0].HasExited);
        Assert.False(_runner.StartedProcesses[1].HasExited);
    }

    [Fact]
    public async Task EnsureRunning_MissingDistro_Fails()
    {
        _runner.Handler = _ => new ProcessResult(0, RunningTable, string.Empty);

        var result = await CreateClient().EnsureRunningAsync("Arch");

        Assert.False(result.Success);
        Assert.Contains("Arch", result.Error);
        Assert.Empty(_runner.StartedProcesses);
    }

    [Fact]
    public async Task EnsureRunning_Wsl1Distro_Fails()
    {
        _runner.Handler = _ => new ProcessResult(0, RunningTable, string.Empty);

        var result = await CreateClient().EnsureRunningAsync("Legacy");

        Assert.False(result.Success);
        Assert.Contains("WSL2", result.Error);
        Assert.Empty(_runner.StartedProcesses);
    }

    [Fact]
    public async Task EnsureRunning_DefaultIsWsl1_FallsBackToFirstWsl2Distro()
    {
        _runner.Handler = _ => new ProcessResult(0, "  NAME STATE VERSION\n* Legacy Stopped 1\n  Ubuntu Running 2\n", string.Empty);

        var result = await CreateClient().EnsureRunningAsync(null);

        Assert.True(result.Success);
        Assert.Contains(_runner.Calls, c => c.CommandLine == "wsl.exe -d Ubuntu --exec sleep infinity");
    }

    [Fact]
    public async Task EnsureRunning_NoWsl2Distro_Fails()
    {
        _runner.Handler = _ => new ProcessResult(-1, "Windows Subsystem for Linux has no installed distributions.", string.Empty);

        var result = await CreateClient().EnsureRunningAsync(null);

        Assert.False(result.Success);
        Assert.Empty(_runner.StartedProcesses);
    }

    [Fact]
    public async Task EnsureRunning_DistroNeverRuns_TimesOutAndStopsKeepAlive()
    {
        _runner.Handler = _ => new ProcessResult(0, StoppedTable, string.Empty);
        var client = CreateClient();

        var result = await client.EnsureRunningAsync(null);

        Assert.False(result.Success);
        Assert.Contains("did not start", result.Error);
        Assert.False(client.IsKeepAliveRunning);
        Assert.True(Assert.Single(_runner.StartedProcesses).HasExited);
    }

    [Fact]
    public async Task StopKeepAlive_And_Dispose_StopTheProcess()
    {
        SimulateDistroStartsWithKeepAlive();
        var client = CreateClient();
        await client.EnsureRunningAsync(null);

        client.StopKeepAlive();

        Assert.False(client.IsKeepAliveRunning);
        Assert.True(_runner.StartedProcesses[0].HasExited);

        await client.EnsureRunningAsync(null);
        client.Dispose();

        Assert.False(client.IsKeepAliveRunning);
        Assert.True(_runner.StartedProcesses[1].HasExited);
    }

    [Fact]
    public async Task RealWsl_ListParsesWithoutThrowing()
    {
        // Read-only call against the real wsl.exe when available.
        var client = new WslClient(new UsbipdManager.Core.Processes.ProcessRunner(_log), _log);
        if (!await client.IsInstalledAsync())
        {
            return;
        }

        var distros = await client.GetDistrosAsync();

        Assert.All(distros, d => Assert.DoesNotContain(' ', d.Name));
        Assert.All(distros, d => Assert.InRange(d.Version, 1, 2));
    }
}

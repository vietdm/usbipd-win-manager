using System.ComponentModel;
using System.Diagnostics;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Processes;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private readonly TestLog _log = new();

    private ProcessRunner CreateRunner() => new(_log);

    [Fact]
    public async Task Run_CapturesStdoutAndStderr()
    {
        var result = await CreateRunner().RunAsync(Cmd, ["/c", "echo", "hello&&", "echo", "oops", "1>&2"]);

        Assert.True(result.Success);
        Assert.Equal("hello", result.StandardOutput.Trim());
        Assert.Equal("oops", result.StandardError.Trim());
    }

    [Fact]
    public async Task Run_ReturnsExitCode()
    {
        var result = await CreateRunner().RunAsync(Cmd, ["/c", "exit", "3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Success);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Run_LargeOutput_DoesNotDeadlock()
    {
        var result = await CreateRunner().RunAsync(
            Cmd,
            ["/c", "for", "/l", "%i", "in", "(1,1,5000)", "do", "@echo", "line%i", "&", "@echo", "err%i", "1>&2"],
            new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(60) });

        Assert.True(result.Success);
        Assert.Contains("line5000", result.StandardOutput);
        Assert.Contains("err5000", result.StandardError);
    }

    [Fact]
    public async Task Run_Timeout_KillsTheProcessTree()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await CreateRunner().RunAsync(
            Cmd,
            ["/c", "ping", "-n", "30", "127.0.0.1"],
            new ProcessRunOptions { Timeout = TimeSpan.FromMilliseconds(500) });

        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Run_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateRunner().RunAsync(Cmd, ["/c", "ping", "-n", "30", "127.0.0.1"], cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Run_PassesEnvironmentVariables()
    {
        var options = new ProcessRunOptions { EnvironmentVariables = new Dictionary<string, string> { ["USBIPD_MANAGER_TEST"] = "it works" } };

        var result = await CreateRunner().RunAsync(Cmd, ["/c", "echo", "%USBIPD_MANAGER_TEST%"], options);

        Assert.Equal("it works", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Run_LogsTheCommandLine()
    {
        await CreateRunner().RunAsync(Cmd, ["/c", "echo", "a b"]);

        var entry = Assert.Single(_log.GetEntries());
        Assert.Equal(LogLevel.Command, entry.Level);
        Assert.Equal("> cmd /c echo \"a b\"", entry.Message);
    }

    [Fact]
    public async Task Run_LogCommandFalse_DoesNotLog()
    {
        await CreateRunner().RunAsync(Cmd, ["/c", "echo", "x"], new ProcessRunOptions { LogCommand = false });

        Assert.Empty(_log.GetEntries());
    }

    [Fact]
    public async Task Run_MissingExecutable_ThrowsWin32Exception()
    {
        await Assert.ThrowsAsync<Win32Exception>(() =>
            CreateRunner().RunAsync(@"C:\does-not-exist\nothing.exe", [], new ProcessRunOptions { LogCommand = false }));
    }

    [Fact]
    public async Task StartLongRunning_Dispose_KillsAndRaisesExited()
    {
        using var process = CreateRunner().StartLongRunning(Cmd, ["/c", "ping", "-n", "30", "127.0.0.1"]);
        var exited = new TaskCompletionSource();
        process.Exited += (_, _) => exited.TrySetResult();

        Assert.False(process.HasExited);
        Assert.True(process.Id > 0);

        process.Dispose();

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task StartLongRunning_ProcessEnds_RaisesExited()
    {
        using var process = CreateRunner().StartLongRunning(Cmd, ["/c", "exit", "0"]);
        var exited = new TaskCompletionSource();
        process.Exited += (_, _) => exited.TrySetResult();
        if (process.HasExited)
        {
            exited.TrySetResult();
        }

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.HasExited);
        Assert.DoesNotContain(_log.GetEntries(), e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(@"C:\Program Files\usbipd-win\usbipd.exe", new[] { "attach", "--wsl", "--busid", "1-4" }, "> usbipd attach --wsl --busid 1-4")]
    [InlineData("sc.exe", new[] { "config", "usbipd", "start=", "auto" }, "> sc config usbipd start= auto")]
    [InlineData("tool", new[] { "", "x y" }, "> tool \"\" \"x y\"")]
    public void CommandLineFormatter_Formats(string fileName, string[] arguments, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.Format(fileName, arguments));
    }
}

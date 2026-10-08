using System.Text;
using System.Xml.Linq;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Platform;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Platform;

public sealed class AutoStartManagerTests
{
    private const string ExePath = @"C:\Tools & Apps\USBIPD <Manager>\UsbipdManager.exe";
    private const string UserId = @"DESKTOP-1\viet";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly FakeProcessRunner _runner = new();
    private readonly TestLog _log = new();

    private AutoStartManager CreateManager(string exePath = ExePath) => new(_runner, _log, exePath, () => UserId);

    private static string QueryXml(string command, bool enabled = true) =>
        $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{UserId}</UserId></LogonTrigger></Triggers>
          <Settings>
            <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd></IdleSettings>
            <Enabled>{(enabled ? "true" : "false")}</Enabled>
          </Settings>
          <Actions Context="Author"><Exec><Command>{System.Security.SecurityElement.Escape(command)}</Command><Arguments>--tray</Arguments></Exec></Actions>
        </Task>
        """;

    [Fact]
    public void Task_xml_contains_the_required_nodes()
    {
        var document = CreateManager().BuildTaskDocument();
        var root = document.Root!;

        Assert.Equal(Ns + "Task", root.Name);
        var trigger = root.Element(Ns + "Triggers")!.Element(Ns + "LogonTrigger")!;
        Assert.Equal(UserId, trigger.Element(Ns + "UserId")!.Value);
        Assert.Equal("PT5S", trigger.Element(Ns + "Delay")!.Value);

        var principal = root.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal(UserId, principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);

        var settings = root.Element(Ns + "Settings")!;
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
        Assert.Equal("true", settings.Element(Ns + "AllowHardTerminate")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StartWhenAvailable")!.Value);

        var exec = root.Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal(ExePath, exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--tray", exec.Element(Ns + "Arguments")!.Value);
        Assert.Equal(@"C:\Tools & Apps\USBIPD <Manager>", exec.Element(Ns + "WorkingDirectory")!.Value);
    }

    [Fact]
    public async Task Enable_writes_a_utf16_task_file_and_calls_schtasks_create()
    {
        string? fileContent = null;
        byte[]? fileStart = null;
        string? xmlPath = null;
        _runner.Handler = call =>
        {
            xmlPath = call.Arguments[4];
            var bytes = File.ReadAllBytes(xmlPath);
            fileStart = bytes[..2];
            fileContent = Encoding.Unicode.GetString(bytes);
            return new ProcessResult(0, "SUCCESS", string.Empty);
        };

        var result = await CreateManager().EnableAsync();

        Assert.True(result.Success);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("schtasks.exe", call.FileName);
        Assert.Equal(["/Create", "/TN", IAutoStartManager.TaskName, "/XML", xmlPath!, "/F"], call.Arguments);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, fileStart);
        Assert.Contains("encoding=\"utf-16\"", fileContent!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Tools &amp; Apps\USBIPD &lt;Manager&gt;\UsbipdManager.exe", fileContent);

        var parsed = XDocument.Parse(fileContent!.TrimStart('\uFEFF'));
        Assert.Equal(ExePath, parsed.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("HighestAvailable", parsed.Descendants(Ns + "RunLevel").Single().Value);

        Assert.False(File.Exists(xmlPath), "The temporary task file must be deleted.");
        Assert.True(_log.Contains(LogLevel.Success, "Start with Windows is on."));
    }

    [Fact]
    public async Task Enable_failure_returns_the_first_stderr_line()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "ERROR: Access is denied.\r\nmore details");

        var result = await CreateManager().EnableAsync();

        Assert.False(result.Success);
        Assert.Contains("ERROR: Access is denied.", result.Error);
        Assert.DoesNotContain("more details", result.Error);
        Assert.False(_log.Contains(LogLevel.Success, "Start with Windows is on."));
    }

    [Fact]
    public async Task Enable_reports_a_missing_schtasks()
    {
        var runner = new ThrowingRunner();
        var manager = new AutoStartManager(runner, _log, ExePath, () => UserId);

        var result = await manager.EnableAsync();

        Assert.False(result.Success);
        Assert.Contains("Start with Windows", result.Error);
    }

    [Fact]
    public async Task Disable_calls_schtasks_delete()
    {
        var result = await CreateManager().DisableAsync();

        Assert.True(result.Success);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("schtasks.exe /Delete /TN USBIPD Manager /F", call.CommandLine);
        Assert.Equal(["/Delete", "/TN", IAutoStartManager.TaskName, "/F"], call.Arguments);
        Assert.True(_log.Contains(LogLevel.Success, "Start with Windows is off."));
    }

    [Fact]
    public async Task Disable_of_a_missing_task_is_a_success()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "ERROR: The system cannot find the file specified.");

        var result = await CreateManager().DisableAsync();

        Assert.True(result.Success);
        Assert.Equal(2, _runner.Calls.Count);
        Assert.Equal("/Query", _runner.Calls[1].Arguments[0]);
    }

    [Fact]
    public async Task Disable_fails_when_the_task_still_exists()
    {
        _runner.Handler = call => call.Arguments[0] == "/Delete"
            ? new ProcessResult(1, string.Empty, "ERROR: Access is denied.")
            : new ProcessResult(0, QueryXml(ExePath), string.Empty);

        var result = await CreateManager().DisableAsync();

        Assert.False(result.Success);
        Assert.Contains("ERROR: Access is denied.", result.Error);
    }

    [Fact]
    public async Task IsEnabled_queries_the_task()
    {
        _runner.Handler = _ => new ProcessResult(0, QueryXml(ExePath), string.Empty);

        Assert.True(await CreateManager().IsEnabledAsync());
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(["/Query", "/TN", IAutoStartManager.TaskName, "/XML"], call.Arguments);
    }

    [Fact]
    public async Task IsEnabled_is_false_for_a_missing_task()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "ERROR: The system cannot find the file specified.");

        Assert.False(await CreateManager().IsEnabledAsync());
    }

    [Fact]
    public async Task IsEnabled_is_false_for_a_disabled_task()
    {
        _runner.Handler = _ => new ProcessResult(0, QueryXml(ExePath, enabled: false), string.Empty);

        Assert.False(await CreateManager().IsEnabledAsync());
    }

    [Fact]
    public void Query_parsing_extracts_command_and_enabled_flag()
    {
        var task = TaskSchedulerXml.Parse(QueryXml(ExePath, enabled: false));

        Assert.NotNull(task);
        Assert.Equal(ExePath, task.Command);
        Assert.False(task.Enabled);
    }

    [Fact]
    public void Query_parsing_falls_back_to_text_scan_for_invalid_xml()
    {
        var broken = "<Task><Settings><Enabled>false</Enabled></Settings><Actions><Exec><Command>\"C:\\A &amp; B\\app.exe\"</Command></Exec></Actions><Broken></Task>";

        var task = TaskSchedulerXml.Parse(broken);

        Assert.NotNull(task);
        Assert.Equal("\"C:\\A & B\\app.exe\"", task.Command);
        Assert.False(task.Enabled);
    }

    [Theory]
    [InlineData(@"C:\Apps\UsbipdManager.exe", @"c:\apps\usbipdmanager.exe", true)]
    [InlineData(@"""C:\Apps\UsbipdManager.exe""", @"C:\Apps\UsbipdManager.exe", true)]
    [InlineData(@"C:\Apps\.\Sub\..\UsbipdManager.exe", @"C:\Apps\UsbipdManager.exe", true)]
    [InlineData(@"C:\Old\UsbipdManager.exe", @"C:\Apps\UsbipdManager.exe", false)]
    [InlineData(null, @"C:\Apps\UsbipdManager.exe", false)]
    public void Path_comparison_is_normalized(string? left, string right, bool expected) =>
        Assert.Equal(expected, TaskSchedulerXml.SamePath(left, right));

    [Fact]
    public async Task EnsurePathUpToDate_does_nothing_when_the_path_matches()
    {
        _runner.Handler = _ => new ProcessResult(0, QueryXml(ExePath.ToUpperInvariant()), string.Empty);

        await CreateManager().EnsurePathUpToDateAsync();

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task EnsurePathUpToDate_does_nothing_without_a_task()
    {
        _runner.Handler = _ => new ProcessResult(1, string.Empty, "ERROR: not found");

        await CreateManager().EnsurePathUpToDateAsync();

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task EnsurePathUpToDate_does_not_re_enable_a_disabled_task()
    {
        _runner.Handler = _ => new ProcessResult(0, QueryXml(@"C:\Old\UsbipdManager.exe", enabled: false), string.Empty);

        await CreateManager().EnsurePathUpToDateAsync();

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task EnsurePathUpToDate_re_registers_a_moved_exe()
    {
        _runner.Handler = call => call.Arguments[0] == "/Query"
            ? new ProcessResult(0, QueryXml(@"C:\Old\UsbipdManager.exe"), string.Empty)
            : new ProcessResult(0, "SUCCESS", string.Empty);

        await CreateManager().EnsurePathUpToDateAsync();

        Assert.Equal(2, _runner.Calls.Count);
        Assert.Equal("/Create", _runner.Calls[1].Arguments[0]);
        Assert.True(_log.Contains(LogLevel.Info, "Start with Windows was updated to the current app location."));
        Assert.False(_log.Contains(LogLevel.Success, "Start with Windows is on."));
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");

        public IManagedProcess StartLongRunning(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null) =>
            throw new System.ComponentModel.Win32Exception(2);
    }
}

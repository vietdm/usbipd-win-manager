using System.ComponentModel;
using System.Security.Principal;
using System.Xml.Linq;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Platform;

public sealed class AutoStartManager : IAutoStartManager
{
    internal const string SchtasksExe = "schtasks.exe";

    private static readonly ProcessRunOptions QueryOptions = new() { LogCommand = false, Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ProcessRunOptions ChangeOptions = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly IProcessRunner _runner;
    private readonly ILogService _log;
    private readonly string _executablePath;
    private readonly Func<string> _userId;

    public AutoStartManager(IProcessRunner runner, ILogService log, string executablePath)
        : this(runner, log, executablePath, CurrentUserName)
    {
    }

    internal AutoStartManager(IProcessRunner runner, ILogService log, string executablePath, Func<string> userId)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(userId);

        _runner = runner;
        _log = log;
        _executablePath = executablePath;
        _userId = userId;
    }

    private static string TaskName => IAutoStartManager.TaskName;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var task = await QueryAsync(cancellationToken).ConfigureAwait(false);
        return task is { Enabled: true };
    }

    public async Task<OperationResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        var result = await RegisterAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _log.Success("Start with Windows is on.");
        }

        return result;
    }

    public async Task<OperationResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(SchtasksExe, ["/Delete", "/TN", TaskName, "/F"], ChangeOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return OperationResult.Fail($"Could not turn off Start with Windows: {ex.Message}");
        }

        if (!result.Success)
        {
            // schtasks messages are localized, so "task not found" is detected by querying instead of parsing the error.
            var stillExists = await QueryAsync(cancellationToken).ConfigureAwait(false) is not null;
            if (stillExists)
            {
                return OperationResult.Fail($"Could not turn off Start with Windows: {Describe(result)}");
            }
        }

        _log.Success("Start with Windows is off.");
        return OperationResult.Ok;
    }

    public async Task EnsurePathUpToDateAsync(CancellationToken cancellationToken = default)
    {
        var task = await QueryAsync(cancellationToken).ConfigureAwait(false);
        if (task is not { Enabled: true } || TaskSchedulerXml.SamePath(task.Command, _executablePath))
        {
            return;
        }

        var result = await RegisterAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _log.Info("Start with Windows was updated to the current app location.");
        }
        else
        {
            _log.Warning(result.Error ?? "Could not update Start with Windows to the current app location.");
        }
    }

    internal XDocument BuildTaskDocument() => TaskSchedulerXml.Build(_executablePath, _userId(), TaskName);

    private async Task<OperationResult> RegisterAsync(CancellationToken cancellationToken)
    {
        string userId;
        try
        {
            userId = _userId();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not turn on Start with Windows: the current user could not be determined ({ex.Message}).");
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), $"UsbipdManager-task-{Guid.NewGuid():N}.xml");
        try
        {
            TaskSchedulerXml.Save(TaskSchedulerXml.Build(_executablePath, userId, TaskName), xmlPath);

            var result = await _runner.RunAsync(SchtasksExe, ["/Create", "/TN", TaskName, "/XML", xmlPath, "/F"], ChangeOptions, cancellationToken)
                .ConfigureAwait(false);
            return result.Success
                ? OperationResult.Ok
                : OperationResult.Fail($"Could not turn on Start with Windows: {Describe(result)}");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return OperationResult.Fail($"Could not turn on Start with Windows: {ex.Message}");
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    /// <summary>Null when the task does not exist (or cannot be read).</summary>
    private async Task<QueriedTask?> QueryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(SchtasksExe, ["/Query", "/TN", TaskName, "/XML"], QueryOptions, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return null;
            }

            // A successful query with unreadable output still means the task exists.
            return TaskSchedulerXml.Parse(result.StandardOutput) ?? new QueriedTask(null, true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Describe(ProcessResult result)
    {
        if (result.TimedOut)
        {
            return "schtasks did not respond in time.";
        }

        var line = FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput);
        return line ?? $"schtasks exited with code {result.ExitCode}.";
    }

    private static string? FirstLine(string? text) =>
        text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string CurrentUserName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless.
        }
    }
}

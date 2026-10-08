using System.ComponentModel;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdClient : IUsbipdClient
{
    internal const string NotInstalledMessage = "usbipd-win is not installed.";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(60);

    // Responses that mean the requested end state is already reached, which keeps the operations idempotent.
    private static readonly string[] AlreadyDone = ["already"];
    private static readonly string[] NotAttached = ["not attached"];
    private static readonly string[] NotShared = ["not shared", "not bound"];

    private readonly IProcessRunner _runner;
    private readonly ILogService _log;
    private readonly Func<string?> _resolveExecutablePath;

    public UsbipdClient(IProcessRunner runner, ILogService log)
        : this(runner, log, null)
    {
    }

    /// <param name="resolveExecutablePath">Test seam; null means the real lookup.</param>
    internal UsbipdClient(IProcessRunner runner, ILogService log, Func<string?>? resolveExecutablePath)
    {
        _runner = runner;
        _log = log;
        _resolveExecutablePath = resolveExecutablePath ?? FindInstalledExecutable;
    }

    public string? ResolveExecutablePath() => _resolveExecutablePath();

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunQueryAsync(["--version"], cancellationToken).ConfigureAwait(false);
        if (result is not { Success: true })
        {
            return null;
        }

        return result.StandardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
    }

    public async Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (ResolveExecutablePath() is null)
        {
            return [];
        }

        try
        {
            var result = await RunQueryAsync(["state"], cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return [];
            }

            if (result.TimedOut)
            {
                _log.Warning("Could not read the device list: usbipd did not respond.");
                return [];
            }

            if (!result.Success)
            {
                _log.Warning($"Could not read the device list: {UsbipdErrors.Extract(result)}");
                return [];
            }

            return UsbipdStateParser.Parse(result.StandardOutput);
        }
        catch (FormatException ex)
        {
            _log.Warning($"Could not read the device list: {ex.Message}");
            return [];
        }
    }

    public Task<OperationResult> BindAsync(string busId, CancellationToken cancellationToken = default) =>
        RunOperationAsync(["bind", "--busid", busId], DefaultTimeout, AlreadyDone, cancellationToken);

    public Task<OperationResult> UnbindAsync(string busId, CancellationToken cancellationToken = default) =>
        RunOperationAsync(["unbind", "--busid", busId], DefaultTimeout, NotShared, cancellationToken);

    public Task<OperationResult> AttachToWslAsync(string busId, CancellationToken cancellationToken = default) =>
        RunOperationAsync(["attach", "--wsl", "--busid", busId], AttachTimeout, AlreadyDone, cancellationToken);

    public Task<OperationResult> DetachAsync(string busId, CancellationToken cancellationToken = default) =>
        RunOperationAsync(["detach", "--busid", busId], DefaultTimeout, NotAttached, cancellationToken);

    public Task<OperationResult> DetachAllAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(["detach", "--all"], DefaultTimeout, NotAttached, cancellationToken);

    internal static string? FindInstalledExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programFiles.Length > 0)
        {
            var defaultPath = Path.Combine(programFiles, "usbipd-win", "usbipd.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }
        }

        return ExecutableLocator.FindOnPath("usbipd.exe");
    }

    /// <summary>Read-only queries are not written to the console: <c>state</c> runs on every device change.</summary>
    private async Task<ProcessResult?> RunQueryAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
        {
            return null;
        }

        try
        {
            var options = new ProcessRunOptions { Timeout = DefaultTimeout, LogCommand = false };
            return await _runner.RunAsync(executable, arguments, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            _log.Warning($"Could not start usbipd: {ex.Message}");
            return null;
        }
    }

    private async Task<OperationResult> RunOperationAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<string> successMarkers,
        CancellationToken cancellationToken)
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
        {
            return OperationResult.Fail(NotInstalledMessage);
        }

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(executable, arguments, new ProcessRunOptions { Timeout = timeout }, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail($"Could not start usbipd: {ex.Message}");
        }

        if (result.TimedOut)
        {
            return OperationResult.Fail($"usbipd did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        if (result.Success)
        {
            return OperationResult.Ok;
        }

        var error = UsbipdErrors.Extract(result);
        return successMarkers.Any(marker => error.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? OperationResult.Ok
            : OperationResult.Fail(error);
    }
}

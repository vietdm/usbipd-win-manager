using System.ComponentModel;
using System.Text;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Wsl;

public sealed class WslClient : IWslClient, IDisposable
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyDictionary<string, string> WslEnvironment = new Dictionary<string, string> { ["WSL_UTF8"] = "1" };

    private readonly IProcessRunner _runner;
    private readonly ILogService _log;
    private readonly string _wslPath;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private readonly Lock _sync = new();

    private IManagedProcess? _keepAlive;
    private string? _keepAliveDistro;
    private string? _keepAliveRequest;

    public WslClient(IProcessRunner runner, ILogService log)
        : this(runner, log, null)
    {
    }

    /// <param name="wslPath">Test seam; null means <c>%SystemRoot%\System32\wsl.exe</c>.</param>
    internal WslClient(IProcessRunner runner, ILogService log, string? wslPath)
    {
        _runner = runner;
        _log = log;
        _wslPath = wslPath ?? Path.Combine(Environment.SystemDirectory, "wsl.exe");
    }

    /// <summary>How often <see cref="EnsureRunningAsync"/> checks whether the distribution is running.</summary>
    internal TimeSpan StartPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    internal TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Test seam for <see cref="IsInstalledAsync"/>; the real check is <see cref="File.Exists(string)"/>.</summary>
    internal Func<string, bool> FileExists { get; set; } = File.Exists;

    public bool IsKeepAliveRunning
    {
        get
        {
            lock (_sync)
            {
                return _keepAlive is { HasExited: false };
            }
        }
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        // Without the WSL feature the wsl.exe stub still exists, but --status fails.
        if (!FileExists(_wslPath))
        {
            return false;
        }

        var result = await RunWslAsync(["--status"], cancellationToken).ConfigureAwait(false);
        return result is { Success: true };
    }

    public async Task<IReadOnlyList<WslDistro>> GetDistrosAsync(CancellationToken cancellationToken = default)
    {
        var (distros, error) = await ListDistrosAsync(cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            _log.Warning($"Could not list WSL distributions: {error}");
        }

        return distros;
    }

    public async Task<OperationResult> EnsureRunningAsync(string? distribution, CancellationToken cancellationToken = default)
    {
        await _ensureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requested = string.IsNullOrWhiteSpace(distribution) ? null : distribution.Trim();
            lock (_sync)
            {
                if (_keepAlive is { HasExited: false } && string.Equals(_keepAliveRequest, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult.Ok;
                }
            }

            var (distros, listError) = await ListDistrosAsync(cancellationToken).ConfigureAwait(false);
            if (listError is not null)
            {
                return OperationResult.Fail($"Could not list WSL distributions: {listError}");
            }

            var (target, resolveError) = ResolveTarget(distros, requested);
            if (target is null)
            {
                return OperationResult.Fail(resolveError!);
            }

            // A keep-alive for another distribution (the setting changed) is replaced.
            StopKeepAlive();

            if (!target.IsRunning)
            {
                _log.Info($"Starting WSL distribution {target.Name}...");
            }

            IManagedProcess process;
            try
            {
                process = _runner.StartLongRunning(
                    _wslPath,
                    ["-d", target.Name, "--exec", "sleep", "infinity"],
                    new ProcessRunOptions { EnvironmentVariables = WslEnvironment, OutputEncoding = Encoding.UTF8 });
            }
            catch (Win32Exception ex)
            {
                return OperationResult.Fail($"Could not start wsl.exe: {ex.Message}");
            }

            lock (_sync)
            {
                _keepAlive = process;
                _keepAliveDistro = target.Name;
                _keepAliveRequest = requested;
            }

            process.Exited += OnKeepAliveExited;

            var result = await WaitUntilRunningAsync(target.Name, process, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                StopKeepAlive();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            StopKeepAlive();
            throw;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public void StopKeepAlive()
    {
        IManagedProcess? process;
        lock (_sync)
        {
            process = _keepAlive;
            _keepAlive = null;
            _keepAliveDistro = null;
            _keepAliveRequest = null;
        }

        if (process is null)
        {
            return;
        }

        process.Exited -= OnKeepAliveExited;
        process.Dispose();
    }

    public void Dispose() => StopKeepAlive();

    private async Task<OperationResult> WaitUntilRunningAsync(string name, IManagedProcess process, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StartTimeout;
        while (true)
        {
            if (process.HasExited)
            {
                return OperationResult.Fail($"WSL distribution {name} could not be started.");
            }

            var (distros, _) = await ListDistrosAsync(cancellationToken).ConfigureAwait(false);
            if (distros.Any(d => d.IsRunning && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Success($"WSL distribution {name} is running.");
                return OperationResult.Ok;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return OperationResult.Fail($"WSL distribution {name} did not start within {StartTimeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(StartPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The requested distribution, else the default one. When the default is WSL1 (or there is no default),
    /// the first WSL2 distribution is used, because only WSL2 can attach USB devices.
    /// </summary>
    private (WslDistro? Target, string? Error) ResolveTarget(IReadOnlyList<WslDistro> distros, string? requested)
    {
        if (requested is not null)
        {
            var match = distros.FirstOrDefault(d => d.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return (null, $"WSL distribution {requested} was not found.");
            }

            return match.Version == 2
                ? (match, null)
                : (null, $"WSL distribution {match.Name} uses WSL1. USB devices need WSL2.");
        }

        var defaultDistro = distros.FirstOrDefault(d => d.IsDefault);
        if (defaultDistro is { Version: 2 })
        {
            return (defaultDistro, null);
        }

        var fallback = distros.FirstOrDefault(d => d.Version == 2);
        if (fallback is null)
        {
            return (null, "No WSL2 distribution found.");
        }

        if (defaultDistro is not null)
        {
            _log.Info($"The default WSL distribution {defaultDistro.Name} uses WSL1. Using {fallback.Name} instead.");
        }

        return (fallback, null);
    }

    private void OnKeepAliveExited(object? sender, EventArgs e)
    {
        string? name;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _keepAlive))
            {
                return;
            }

            name = _keepAliveDistro;
            _keepAlive = null;
            _keepAliveDistro = null;
            _keepAliveRequest = null;
        }

        _log.Warning($"The WSL keep-alive process for {name} ended.");
    }

    /// <summary>Read-only and polled while starting a distribution, so the command is not written to the console.</summary>
    private async Task<(IReadOnlyList<WslDistro> Distros, string? Error)> ListDistrosAsync(CancellationToken cancellationToken)
    {
        if (!FileExists(_wslPath))
        {
            return ([], null);
        }

        var result = await RunWslAsync(["-l", "-v"], cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return ([], "wsl.exe could not be started.");
        }

        if (result.TimedOut)
        {
            return ([], "wsl.exe did not respond.");
        }

        // With WSL but no distribution, wsl.exe prints a message instead of the table and may exit non-zero.
        return (WslListParser.Parse(result.StandardOutput), null);
    }

    private async Task<ProcessResult?> RunWslAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var options = new ProcessRunOptions
        {
            Timeout = QueryTimeout,
            LogCommand = false,
            OutputEncoding = Encoding.UTF8,
            EnvironmentVariables = WslEnvironment,
        };

        try
        {
            return await _runner.RunAsync(_wslPath, arguments, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

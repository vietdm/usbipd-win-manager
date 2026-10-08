using System.ComponentModel;
using System.Diagnostics;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>How long to wait for the output pipes to close after the process tree was killed.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogService _log;

    public ProcessRunner(ILogService log)
    {
        _log = log;
    }

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ProcessRunOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        if (options.LogCommand)
        {
            _log.Command(CommandLineFormatter.Format(fileName, arguments));
        }

        using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments, options) };
        process.Start();

        // Closing stdin makes any unexpected prompt read EOF instead of hanging until the timeout.
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            // A grandchild can inherit the pipes and keep them open after the process exits, so the timeout applies here too.
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            KillProcessTree(process);
            var (stdout, stderr) = await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(-1, stdout, stderr, TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public IManagedProcess StartLongRunning(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null)
    {
        options ??= ProcessRunOptions.Default;

        if (options.LogCommand)
        {
            _log.Command(CommandLineFormatter.Format(fileName, arguments));
        }

        var process = new Process
        {
            StartInfo = CreateStartInfo(fileName, arguments, options),
            EnableRaisingEvents = true,
        };

        var managed = new ManagedProcess(process);
        try
        {
            managed.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        if (!JobObject.TryAssign(process, out var error))
        {
            _log.Warning($"Could not tie {Path.GetFileName(fileName)} to the app lifetime ({error}). It may keep running if the app crashes.");
        }

        return managed;
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions options)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = options.OutputEncoding,
            StandardErrorEncoding = options.OutputEncoding,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach (var (name, value) in options.EnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    internal static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Access denied while the process is terminating.
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> DrainAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(DrainTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
        }

        return (CompletedText(stdoutTask), CompletedText(stderrTask));
    }

    private static string CompletedText(Task<string> task) => task.IsCompletedSuccessfully ? task.Result : string.Empty;
}

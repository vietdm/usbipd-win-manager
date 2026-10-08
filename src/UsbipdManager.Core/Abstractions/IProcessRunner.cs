using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IProcessRunner
{
    /// <summary>Runs a process with no window and captures its output. Never throws for a non-zero exit code.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable cannot be started (e.g. not found).</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Starts a hidden process that keeps running until it exits or is disposed.</summary>
    IManagedProcess StartLongRunning(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null);
}

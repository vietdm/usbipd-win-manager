using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessRunner(ILogService log)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public IManagedProcess StartLongRunning(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null)
        => throw new NotImplementedException();
}

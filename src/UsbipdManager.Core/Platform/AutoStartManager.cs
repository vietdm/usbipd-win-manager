using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Platform;

public sealed class AutoStartManager : IAutoStartManager
{
    public AutoStartManager(IProcessRunner runner, ILogService log, string executablePath)
    {
        throw new NotImplementedException();
    }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> EnableAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> DisableAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task EnsurePathUpToDateAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

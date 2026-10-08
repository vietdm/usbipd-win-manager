using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Wsl;

public sealed class WslClient : IWslClient, IDisposable
{
    public WslClient(IProcessRunner runner, ILogService log)
    {
        throw new NotImplementedException();
    }

    public bool IsKeepAliveRunning => throw new NotImplementedException();

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<IReadOnlyList<WslDistro>> GetDistrosAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> EnsureRunningAsync(string? distribution, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void StopKeepAlive() => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

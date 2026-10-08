using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdClient : IUsbipdClient
{
    public UsbipdClient(IProcessRunner runner, ILogService log)
    {
        throw new NotImplementedException();
    }

    public string? ResolveExecutablePath() => throw new NotImplementedException();

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> BindAsync(string busId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> UnbindAsync(string busId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> AttachToWslAsync(string busId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> DetachAsync(string busId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> DetachAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

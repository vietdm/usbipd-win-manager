using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IUsbipdClient
{
    /// <summary>Full path of usbipd.exe, re-resolved on every call (it can be installed while the app runs); null if not installed.</summary>
    string? ResolveExecutablePath();

    Task<string?> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Connected devices plus bound devices that are not plugged in, from <c>usbipd state</c>.</summary>
    Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> BindAsync(string busId, CancellationToken cancellationToken = default);

    Task<OperationResult> UnbindAsync(string busId, CancellationToken cancellationToken = default);

    /// <summary>Requires a running WSL2 distribution.</summary>
    Task<OperationResult> AttachToWslAsync(string busId, CancellationToken cancellationToken = default);

    Task<OperationResult> DetachAsync(string busId, CancellationToken cancellationToken = default);

    Task<OperationResult> DetachAllAsync(CancellationToken cancellationToken = default);
}

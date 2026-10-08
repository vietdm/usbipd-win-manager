using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IModeSwitcher
{
    UsbMode Mode { get; }

    /// <summary>Detaches everything, stops the WSL keep-alive, saves the mode.</summary>
    Task<OperationResult> SwitchToWindowsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures WSL is running, attaches connected managed devices, saves the mode.</summary>
    Task<OperationResult> SwitchToWslAsync(CancellationToken cancellationToken = default);

    /// <summary>Exit path: detaches everything and stops the keep-alive WITHOUT changing the saved mode.</summary>
    Task<OperationResult> ReleaseForExitAsync(CancellationToken cancellationToken = default);
}

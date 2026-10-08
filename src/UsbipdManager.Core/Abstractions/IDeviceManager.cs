using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>Owns the device list and the managed-device rules (section 6.4 of docs/PLAN.md).</summary>
public interface IDeviceManager
{
    event EventHandler? EntriesChanged;

    IReadOnlyList<DeviceEntry> Entries { get; }

    /// <summary>
    /// Reads <c>usbipd state</c>, rebuilds <see cref="Entries"/> and applies the rules: a managed device back on its port is bound,
    /// and attached when the mode is WSL2 and auto re-attach is on.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default);

    /// <summary>Removes a remembered entry that is disconnected.</summary>
    void Forget(DeviceEntry entry);

    Task<OperationResult> BindConnectedManagedAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> AttachConnectedManagedAsync(CancellationToken cancellationToken = default);
}

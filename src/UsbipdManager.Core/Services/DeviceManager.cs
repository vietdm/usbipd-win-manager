using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class DeviceManager : IDeviceManager
{
    public DeviceManager(IUsbipdClient usbipd, IWslClient wsl, ISettingsStore settings, ILogService log)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? EntriesChanged;

    public IReadOnlyList<DeviceEntry> Entries => throw new NotImplementedException();

    public Task RefreshAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void Forget(DeviceEntry entry) => throw new NotImplementedException();

    public Task<OperationResult> BindConnectedManagedAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> AttachConnectedManagedAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class AppController : IAppController, IDisposable
{
    public AppController(
        IEnvironmentChecker checker,
        IInitService init,
        IDeviceManager devices,
        IModeSwitcher modes,
        IDeviceChangeNotifier deviceChanges,
        IOperationGate gate,
        ISettingsStore settings,
        ILogService log)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? StateChanged;

    public EnvironmentReport? Report => throw new NotImplementedException();

    public UsbMode Mode => throw new NotImplementedException();

    public bool IsBusy => throw new NotImplementedException();

    public bool CanSwitch => throw new NotImplementedException();

    public IReadOnlyList<DeviceEntry> Devices => throw new NotImplementedException();

    public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task InitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task SwitchToWindowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task SwitchToWslAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task ForgetAsync(DeviceEntry entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

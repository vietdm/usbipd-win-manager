using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class ModeSwitcher : IModeSwitcher
{
    public ModeSwitcher(IUsbipdClient usbipd, IWslClient wsl, IDeviceManager devices, ISettingsStore settings, ILogService log)
    {
        throw new NotImplementedException();
    }

    public UsbMode Mode => throw new NotImplementedException();

    public Task<OperationResult> SwitchToWindowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> SwitchToWslAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> ReleaseForExitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

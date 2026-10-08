using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class ModeSwitcher : IModeSwitcher
{
    private readonly IUsbipdClient _usbipd;
    private readonly IWslClient _wsl;
    private readonly IDeviceManager _devices;
    private readonly ISettingsStore _settings;
    private readonly ILogService _log;

    public ModeSwitcher(IUsbipdClient usbipd, IWslClient wsl, IDeviceManager devices, ISettingsStore settings, ILogService log)
    {
        _usbipd = usbipd;
        _wsl = wsl;
        _devices = devices;
        _settings = settings;
        _log = log;
    }

    public UsbMode Mode => _settings.Current.Mode;

    public async Task<OperationResult> SwitchToWindowsAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureUsbipdInstalled())
        {
            return OperationResult.Fail("usbipd-win is not installed.");
        }

        _log.Info("Switching to Windows...");
        var detach = await _usbipd.DetachAllAsync(cancellationToken).ConfigureAwait(false);
        _wsl.StopKeepAlive();

        // The user chose Windows: save it even if a detach failed, so nothing is attached automatically anymore.
        SaveMode(UsbMode.Windows);
        if (!detach.Success)
        {
            _log.Error($"Switched to Windows, but some devices could not be detached: {DeviceText.Reason(detach)}");
            return detach;
        }

        _log.Success("Switched to Windows.");
        return OperationResult.Ok;
    }

    public async Task<OperationResult> SwitchToWslAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureUsbipdInstalled())
        {
            return OperationResult.Fail("usbipd-win is not installed.");
        }

        _log.Info("Switching to WSL2...");
        var wsl = await _wsl.EnsureRunningAsync(_settings.Current.WslDistribution, cancellationToken).ConfigureAwait(false);
        if (!wsl.Success)
        {
            _log.Error($"Could not start WSL 2, the mode was not changed: {DeviceText.Reason(wsl)}");
            return wsl;
        }

        SaveMode(UsbMode.Wsl);
        var attach = await _devices.AttachConnectedManagedAsync(cancellationToken).ConfigureAwait(false);
        if (!_devices.Entries.Any(e => e.IsManaged && e.IsConnected))
        {
            _log.Warning("No managed device is connected. Turn on a device's switch to move it to WSL2.");
        }

        if (!attach.Success)
        {
            _log.Warning($"Switched to WSL2, but some devices are not attached. {DeviceText.Reason(attach)}");
            return attach;
        }

        _log.Success("Switched to WSL2.");
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ReleaseForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_usbipd.ResolveExecutablePath() is null)
            {
                return OperationResult.Ok;
            }

            var detach = await _usbipd.DetachAllAsync(cancellationToken).ConfigureAwait(false);
            if (!detach.Success)
            {
                _log.Error($"Could not return devices to Windows: {DeviceText.Reason(detach)}");
            }

            return detach;
        }
        finally
        {
            _wsl.StopKeepAlive();
        }
    }

    private bool EnsureUsbipdInstalled()
    {
        if (_usbipd.ResolveExecutablePath() is not null)
        {
            return true;
        }

        _log.Error("usbipd-win is not installed. Press Init to install it.");
        return false;
    }

    private void SaveMode(UsbMode mode)
    {
        if (_settings.Current.Mode != mode)
        {
            _settings.Update(s => s.Mode = mode);
        }
    }
}

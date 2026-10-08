using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Platform;

namespace UsbipdManager.Core.Services;

public sealed class EnvironmentChecker : IEnvironmentChecker
{
    private readonly IUsbipdClient _usbipd;
    private readonly IWslClient _wsl;
    private readonly IUsbipdServiceController _service;
    private readonly ILogService _log;
    private readonly Func<bool> _isElevated;

    public EnvironmentChecker(IUsbipdClient usbipd, IWslClient wsl, IUsbipdServiceController service, ILogService log)
        : this(usbipd, wsl, service, log, Elevation.IsElevated)
    {
    }

    internal EnvironmentChecker(IUsbipdClient usbipd, IWslClient wsl, IUsbipdServiceController service, ILogService log, Func<bool> isElevated)
    {
        _usbipd = usbipd;
        _wsl = wsl;
        _service = service;
        _log = log;
        _isElevated = isElevated;
    }

    public async Task<EnvironmentReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        _log.Info("Checking environment...");

        var elevated = Check("administrator rights", () =>
        {
            var value = _isElevated();
            if (value)
            {
                _log.Success("Running as administrator.");
            }
            else
            {
                _log.Error("Not running as administrator. Restart USBIPD Manager as administrator.");
            }

            return value;
        }, false);

        var (usbipdInstalled, version) = await CheckAsync("usbipd-win", async () =>
        {
            if (_usbipd.ResolveExecutablePath() is null)
            {
                _log.Error("usbipd-win is not installed. Press Init to install it.");
                return (false, (string?)null);
            }

            var value = await _usbipd.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            _log.Success(string.IsNullOrWhiteSpace(value) ? "usbipd-win is installed." : $"usbipd-win {value} is installed.");
            return (true, value);
        }, (false, null)).ConfigureAwait(false);

        var service = Check("the usbipd service", () =>
        {
            var info = _service.GetInfo();
            if (!info.Exists)
            {
                if (usbipdInstalled)
                {
                    _log.Error("usbipd service was not found although usbipd-win is installed. Reinstall usbipd-win.");
                }
            }
            else if (info.StartupType == ServiceStartupType.Disabled)
            {
                _log.Warning("usbipd service is disabled. Press Init to enable and start it.");
            }
            else if (info.IsRunning)
            {
                _log.Success("usbipd service is running.");
            }
            else
            {
                _log.Warning("usbipd service is stopped. Press Init to start it.");
            }

            return info;
        }, UsbipdServiceInfo.NotFound);

        var wslInstalled = await CheckAsync("WSL", async () =>
        {
            var value = await _wsl.IsInstalledAsync(cancellationToken).ConfigureAwait(false);
            if (!value)
            {
                _log.Error("WSL is not installed on this machine. USBIPD Manager is not supported.");
            }

            return value;
        }, false).ConfigureAwait(false);

        IReadOnlyList<WslDistro> distros = [];
        if (wslInstalled)
        {
            distros = await CheckAsync("WSL distributions", async () =>
            {
                var list = await _wsl.GetDistrosAsync(cancellationToken).ConfigureAwait(false);
                foreach (var distro in list)
                {
                    LogDistro(distro);
                }

                if (!list.Any(d => d.Version == 2))
                {
                    _log.Error("No WSL 2 distribution found. USBIPD Manager is not supported.");
                }

                return list;
            }, (IReadOnlyList<WslDistro>)[]).ConfigureAwait(false);
        }

        IReadOnlyList<UsbDevice> devices = [];
        if (usbipdInstalled)
        {
            devices = await CheckAsync("USB devices", async () =>
            {
                var list = await _usbipd.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                var connected = list
                    .Where(d => d.IsConnected)
                    .OrderBy(d => d.BusId, NaturalStringComparer.Instance)
                    .ToList();
                _log.Info($"Found {connected.Count} USB device(s).");
                foreach (var device in connected)
                {
                    _log.Info(DeviceText.Line(device));
                }

                return list;
            }, (IReadOnlyList<UsbDevice>)[]).ConfigureAwait(false);
        }

        return new EnvironmentReport(elevated, usbipdInstalled, version, service, wslInstalled, distros, devices);
    }

    private void LogDistro(WslDistro distro)
    {
        var state = distro.IsRunning ? "running" : "stopped";
        var suffix = distro.IsDefault ? ", default" : string.Empty;
        if (distro.Version == 2)
        {
            _log.Success($"WSL 2 distribution: {distro.Name} ({state}{suffix})");
        }
        else
        {
            _log.Info($"WSL {distro.Version} distribution: {distro.Name} ({state}{suffix}) is not usable. Convert it with: wsl --set-version {distro.Name} 2");
        }
    }

    private T Check<T>(string what, Func<T> check, T fallback)
    {
        try
        {
            return check();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Could not check {what}: {ex.Message}");
            return fallback;
        }
    }

    private async Task<T> CheckAsync<T>(string what, Func<Task<T>> check, T fallback)
    {
        try
        {
            return await check().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Could not check {what}: {ex.Message}");
            return fallback;
        }
    }
}

using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class InitService : IInitService
{
    private readonly IEnvironmentChecker _checker;
    private readonly IUsbipdInstaller _installer;
    private readonly IUsbipdServiceController _service;
    private readonly IDeviceManager _devices;
    private readonly ILogService _log;

    public InitService(IEnvironmentChecker checker, IUsbipdInstaller installer, IUsbipdServiceController service, IDeviceManager devices, ILogService log)
    {
        _checker = checker;
        _installer = installer;
        _service = service;
        _devices = devices;
        _log = log;
    }

    public async Task<EnvironmentReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!report.IsSupported)
        {
            _log.Error("Init stopped: WSL 2 is required.");
            return report;
        }

        var usbipdInstalled = report.UsbipdInstalled;
        if (!usbipdInstalled)
        {
            usbipdInstalled = await StepAsync("Installing usbipd-win", InstallUsbipdAsync, cancellationToken).ConfigureAwait(false);
        }

        if (usbipdInstalled)
        {
            var serviceReady = await StepAsync("Starting the usbipd service", PrepareServiceAsync, cancellationToken).ConfigureAwait(false);
            if (serviceReady)
            {
                await StepAsync(
                    "Sharing managed devices",
                    async ct => (await _devices.BindConnectedManagedAsync(ct).ConfigureAwait(false)).Success,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var final = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        var notReady = NotReady(final);
        if (notReady.Count == 0)
        {
            _log.Success("Init completed.");
        }
        else
        {
            _log.Warning($"Init completed, but some items are not ready: {string.Join("; ", notReady)}.");
        }

        return final;
    }

    private async Task<bool> InstallUsbipdAsync(CancellationToken cancellationToken)
    {
        if (!_installer.IsWingetAvailable())
        {
            _log.Error($"winget is not available. Download and install usbipd-win from {IUsbipdInstaller.ReleasePageUrl}, then press Init again.");
            return false;
        }

        _log.Info("Installing usbipd-win with winget. This can take a few minutes...");
        var result = await _installer.InstallAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _log.Error($"usbipd-win could not be installed. Install it from {IUsbipdInstaller.ReleasePageUrl}, then press Init again. Details: {DeviceText.Reason(result)}");
            return false;
        }

        _log.Success("usbipd-win was installed.");
        return true;
    }

    private async Task<bool> PrepareServiceAsync(CancellationToken cancellationToken)
    {
        var info = _service.GetInfo();
        if (!info.Exists)
        {
            _log.Error("usbipd service was not found. Reinstall usbipd-win, then press Init again.");
            return false;
        }

        if (info.StartupType == ServiceStartupType.Disabled)
        {
            _log.Info("Setting the usbipd service to start automatically...");
            var auto = await _service.SetAutomaticStartAsync(cancellationToken).ConfigureAwait(false);
            if (!auto.Success)
            {
                _log.Error($"Could not enable the usbipd service: {DeviceText.Reason(auto)}");
                return false;
            }
        }

        if (info.IsRunning)
        {
            return true;
        }

        _log.Info("Starting the usbipd service...");
        var start = await _service.StartAsync(cancellationToken).ConfigureAwait(false);
        if (!start.Success)
        {
            _log.Error($"Could not start the usbipd service: {DeviceText.Reason(start)}");
            return false;
        }

        _log.Success("usbipd service started.");
        return true;
    }

    private async Task<bool> StepAsync(string what, Func<CancellationToken, Task<bool>> step, CancellationToken cancellationToken)
    {
        try
        {
            return await step(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"{what} failed: {ex.Message}");
            return false;
        }
    }

    private static List<string> NotReady(EnvironmentReport report)
    {
        var items = new List<string>();
        if (!report.IsElevated)
        {
            items.Add("not running as administrator");
        }

        if (!report.UsbipdInstalled)
        {
            items.Add("usbipd-win is not installed");
        }
        else if (!report.UsbipdService.IsRunning)
        {
            items.Add("usbipd service is not running");
        }

        if (!report.IsSupported)
        {
            items.Add("no WSL 2 distribution");
        }

        return items;
    }
}

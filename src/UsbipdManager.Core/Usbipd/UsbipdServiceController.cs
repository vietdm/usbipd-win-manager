using System.ComponentModel;
using System.ServiceProcess;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdServiceController : IUsbipdServiceController
{
    public const string ServiceName = "usbipd";

    private const int ErrorServiceDoesNotExist = 1060;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IProcessRunner _runner;
    private readonly ILogService _log;

    public UsbipdServiceController(IProcessRunner runner, ILogService log)
    {
        _runner = runner;
        _log = log;
    }

    public UsbipdServiceInfo GetInfo()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            return new UsbipdServiceInfo(
                Exists: true,
                IsRunning: service.Status == ServiceControllerStatus.Running,
                StartupType: MapStartType(service.StartType));
        }
        catch (InvalidOperationException ex) when (IsServiceMissing(ex))
        {
            return UsbipdServiceInfo.NotFound;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The service exists but cannot be queried (e.g. access denied).
            _log.Warning($"Could not read the usbipd service state: {ex.Message}");
            return new UsbipdServiceInfo(true, false, ServiceStartupType.Unknown);
        }
    }

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => StartCoreAsync(cancellationToken), cancellationToken);

    public async Task<OperationResult> SetAutomaticStartAsync(CancellationToken cancellationToken = default)
    {
        var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(scPath, ["config", ServiceName, "start=", "auto"], new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(30) }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail($"Could not start sc.exe: {ex.Message}");
        }

        if (result.Success)
        {
            _log.Success("The usbipd service now starts automatically.");
            return OperationResult.Ok;
        }

        if (result.TimedOut)
        {
            return OperationResult.Fail("sc.exe did not respond.");
        }

        // sc.exe prints "[SC] ChangeServiceConfig FAILED 5:" followed by the reason ("Access is denied.").
        var lastLine = (result.StandardOutput + "\n" + result.StandardError)
            .Split('\n')
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0);
        return OperationResult.Fail(lastLine ?? $"sc.exe exited with code {result.ExitCode}.");
    }

    private async Task<OperationResult> StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                return OperationResult.Ok;
            }

            if (service.StartType == ServiceStartMode.Disabled)
            {
                return OperationResult.Fail("The usbipd service is disabled.");
            }

            _log.Info("Starting the usbipd service...");
            if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.Paused)
            {
                if (service.Status == ServiceControllerStatus.Paused)
                {
                    service.Continue();
                }
                else
                {
                    service.Start();
                }
            }

            var deadline = DateTime.UtcNow + StartTimeout;
            while (true)
            {
                service.Refresh();
                if (service.Status == ServiceControllerStatus.Running)
                {
                    _log.Success("The usbipd service is running.");
                    return OperationResult.Ok;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return OperationResult.Fail($"The usbipd service did not start within {StartTimeout.TotalSeconds:0} seconds.");
                }

                await Task.Delay(StatusPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex) when (IsServiceMissing(ex))
        {
            return OperationResult.Fail("The usbipd service is not installed.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            var reason = ex.InnerException is Win32Exception inner ? inner.Message : ex.Message;
            return OperationResult.Fail($"Could not start the usbipd service: {reason}");
        }
    }

    private static bool IsServiceMissing(InvalidOperationException ex) =>
        ex.InnerException is Win32Exception { NativeErrorCode: ErrorServiceDoesNotExist };

    private static ServiceStartupType MapStartType(ServiceStartMode mode) => mode switch
    {
        // Automatic (Delayed Start) is also reported as Automatic.
        ServiceStartMode.Automatic or ServiceStartMode.Boot or ServiceStartMode.System => ServiceStartupType.Automatic,
        ServiceStartMode.Manual => ServiceStartupType.Manual,
        ServiceStartMode.Disabled => ServiceStartupType.Disabled,
        _ => ServiceStartupType.Unknown,
    };
}

using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>The <c>usbipd</c> Windows service installed by usbipd-win.</summary>
public interface IUsbipdServiceController
{
    UsbipdServiceInfo GetInfo();

    Task<OperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SetAutomaticStartAsync(CancellationToken cancellationToken = default);
}

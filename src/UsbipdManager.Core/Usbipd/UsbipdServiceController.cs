using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdServiceController : IUsbipdServiceController
{
    public const string ServiceName = "usbipd";

    public UsbipdServiceController(IProcessRunner runner, ILogService log)
    {
        throw new NotImplementedException();
    }

    public UsbipdServiceInfo GetInfo() => throw new NotImplementedException();

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<OperationResult> SetAutomaticStartAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

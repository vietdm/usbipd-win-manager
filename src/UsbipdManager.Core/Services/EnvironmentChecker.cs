using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class EnvironmentChecker : IEnvironmentChecker
{
    public EnvironmentChecker(IUsbipdClient usbipd, IWslClient wsl, IUsbipdServiceController service, ILogService log)
    {
        throw new NotImplementedException();
    }

    public Task<EnvironmentReport> CheckAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

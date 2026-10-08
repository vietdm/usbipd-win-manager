using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

public sealed class InitService : IInitService
{
    public InitService(IEnvironmentChecker checker, IUsbipdInstaller installer, IUsbipdServiceController service, IDeviceManager devices, ILogService log)
    {
        throw new NotImplementedException();
    }

    public Task<EnvironmentReport> RunAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

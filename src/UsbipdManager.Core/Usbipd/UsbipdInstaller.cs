using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdInstaller : IUsbipdInstaller
{
    public const string WingetPackageId = "dorssel.usbipd-win";

    public UsbipdInstaller(IProcessRunner runner, ILogService log)
    {
        throw new NotImplementedException();
    }

    public bool IsWingetAvailable() => throw new NotImplementedException();

    public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

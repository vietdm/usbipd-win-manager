using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IUsbipdInstaller
{
    const string ReleasePageUrl = "https://github.com/dorssel/usbipd-win/releases/latest";

    bool IsWingetAvailable();

    /// <summary>Installs usbipd-win silently with winget. Can take minutes.</summary>
    Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default);
}

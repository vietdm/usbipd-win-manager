using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IInitService
{
    /// <summary>Checks, then installs usbipd-win / starts its service / binds managed devices where needed. Returns the final report.</summary>
    Task<EnvironmentReport> RunAsync(CancellationToken cancellationToken = default);
}

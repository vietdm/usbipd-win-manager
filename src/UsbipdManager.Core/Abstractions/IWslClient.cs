using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IWslClient
{
    bool IsKeepAliveRunning { get; }

    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WslDistro>> GetDistrosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure a WSL2 distribution is running and stays running, by starting a hidden keep-alive process owned by the app.
    /// <paramref name="distribution"/> null means the default distribution.
    /// </summary>
    Task<OperationResult> EnsureRunningAsync(string? distribution, CancellationToken cancellationToken = default);

    void StopKeepAlive();
}

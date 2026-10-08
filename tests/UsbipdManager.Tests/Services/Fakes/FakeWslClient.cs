using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeWslClient : IWslClient
{
    public bool Installed { get; set; } = true;

    public List<WslDistro> Distros { get; } = [new("Ubuntu", false, 2, true)];

    public Exception? InstalledException { get; set; }

    public OperationResult EnsureRunningResult { get; set; } = OperationResult.Ok;

    public List<string?> EnsureRunningCalls { get; } = [];

    public int StopKeepAliveCalls { get; private set; }

    public bool IsKeepAliveRunning { get; private set; }

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
        InstalledException is null ? Task.FromResult(Installed) : throw InstalledException;

    public Task<IReadOnlyList<WslDistro>> GetDistrosAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WslDistro>>([.. Distros]);

    public Task<OperationResult> EnsureRunningAsync(string? distribution, CancellationToken cancellationToken = default)
    {
        EnsureRunningCalls.Add(distribution);
        IsKeepAliveRunning = EnsureRunningResult.Success;
        return Task.FromResult(EnsureRunningResult);
    }

    public void StopKeepAlive()
    {
        StopKeepAliveCalls++;
        IsKeepAliveRunning = false;
    }
}

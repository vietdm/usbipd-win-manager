using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeDeviceManager : IDeviceManager
{
    private int _refreshCalls;

    public event EventHandler? EntriesChanged;

    public IReadOnlyList<DeviceEntry> Entries { get; set; } = [];

    public int RefreshCalls => Volatile.Read(ref _refreshCalls);

    /// <summary>When set, <see cref="RefreshAsync"/> waits for it (to keep the gate busy).</summary>
    public Func<CancellationToken, Task>? RefreshHook { get; set; }

    public Exception? RefreshException { get; set; }

    public List<(DeviceEntry Entry, bool Managed)> SetManagedCalls { get; } = [];

    public List<DeviceEntry> Forgotten { get; } = [];

    public int AttachCalls { get; private set; }

    public int BindCalls { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshCalls);
        if (RefreshHook is not null)
        {
            await RefreshHook(cancellationToken);
        }

        if (RefreshException is not null)
        {
            throw RefreshException;
        }
    }

    public Task<OperationResult> SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default)
    {
        SetManagedCalls.Add((entry, managed));
        return Task.FromResult(OperationResult.Ok);
    }

    public void Forget(DeviceEntry entry) => Forgotten.Add(entry);

    public Task<OperationResult> BindConnectedManagedAsync(CancellationToken cancellationToken = default)
    {
        BindCalls++;
        return Task.FromResult(OperationResult.Ok);
    }

    public Task<OperationResult> AttachConnectedManagedAsync(CancellationToken cancellationToken = default)
    {
        AttachCalls++;
        return Task.FromResult(OperationResult.Ok);
    }

    public void RaiseEntriesChanged() => EntriesChanged?.Invoke(this, EventArgs.Empty);
}

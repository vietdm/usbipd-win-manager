namespace UsbipdManager.Core.Abstractions;

/// <summary>
/// Serializes user-visible operations (init, mode switch, device toggles, refresh) so they never interleave.
/// Only <see cref="IAppController"/> uses it; inner services are not gated, so they can call each other without deadlocking.
/// </summary>
public interface IOperationGate
{
    bool IsBusy { get; }

    event EventHandler? BusyChanged;

    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>Runs only if nothing else is running; returns false when skipped.</summary>
    Task<bool> TryRunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

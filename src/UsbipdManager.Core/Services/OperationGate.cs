using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Services;

public sealed class OperationGate : IOperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsBusy => _semaphore.CurrentCount == 0;

    /// <summary>Raised after entering and after leaving an operation; read <see cref="IsBusy"/> for the current state.</summary>
    public event EventHandler? BusyChanged;

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await RunEnteredAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryRunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await RunEnteredAsync(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunEnteredAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

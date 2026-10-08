using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Services;

public sealed class OperationGate : IOperationGate
{
    public bool IsBusy => throw new NotImplementedException();

    public event EventHandler? BusyChanged;

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> TryRunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

namespace UsbipdManager.Core.Abstractions;

/// <summary>A long-running child process owned by the app (e.g. the WSL keep-alive). Dispose kills it.</summary>
public interface IManagedProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    event EventHandler? Exited;
}

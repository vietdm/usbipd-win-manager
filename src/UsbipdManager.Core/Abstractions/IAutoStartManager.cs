using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>Start with Windows through a Task Scheduler logon task with highest privileges (no UAC prompt at logon).</summary>
public interface IAutoStartManager
{
    const string TaskName = "USBIPD Manager";

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> EnableAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-registers the task when it exists but points to another exe path (portable exe moved).</summary>
    Task EnsurePathUpToDateAsync(CancellationToken cancellationToken = default);
}

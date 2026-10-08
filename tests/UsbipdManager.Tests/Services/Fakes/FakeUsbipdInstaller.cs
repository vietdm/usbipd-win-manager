using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeUsbipdInstaller : IUsbipdInstaller
{
    public bool WingetAvailable { get; set; } = true;

    public OperationResult InstallResult { get; set; } = OperationResult.Ok;

    /// <summary>Runs on a successful install, e.g. to make the fake usbipd client "installed".</summary>
    public Action? OnInstalled { get; set; }

    public int InstallCalls { get; private set; }

    public bool IsWingetAvailable() => WingetAvailable;

    public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        if (InstallResult.Success)
        {
            OnInstalled?.Invoke();
        }

        return Task.FromResult(InstallResult);
    }
}

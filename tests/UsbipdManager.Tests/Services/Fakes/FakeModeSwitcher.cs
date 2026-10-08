using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeModeSwitcher(ISettingsStore settings) : IModeSwitcher
{
    public List<string> Calls { get; } = [];

    public Exception? SwitchException { get; set; }

    public UsbMode Mode => settings.Current.Mode;

    public Task<OperationResult> SwitchToWindowsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("windows");
        ThrowIfConfigured();
        settings.Update(s => s.Mode = UsbMode.Windows);
        return Task.FromResult(OperationResult.Ok);
    }

    public Task<OperationResult> SwitchToWslAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("wsl");
        ThrowIfConfigured();
        settings.Update(s => s.Mode = UsbMode.Wsl);
        return Task.FromResult(OperationResult.Ok);
    }

    public Task<OperationResult> ReleaseForExitAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("release");
        return Task.FromResult(OperationResult.Ok);
    }

    private void ThrowIfConfigured()
    {
        if (SwitchException is not null)
        {
            throw SwitchException;
        }
    }
}

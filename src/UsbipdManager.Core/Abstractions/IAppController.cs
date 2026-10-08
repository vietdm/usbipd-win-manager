using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>
/// The single entry point the UI talks to. Every action runs through <see cref="IOperationGate"/>.
/// <see cref="StateChanged"/> may be raised on any thread.
/// </summary>
public interface IAppController
{
    event EventHandler? StateChanged;

    EnvironmentReport? Report { get; }

    UsbMode Mode { get; }

    bool IsBusy { get; }

    /// <summary>The machine is supported, usbipd is ready and nothing is running.</summary>
    bool CanSwitch { get; }

    IReadOnlyList<DeviceEntry> Devices { get; }

    /// <summary>Startup checks, device monitoring, then restores the saved mode if the setting allows it.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    Task InitAsync(CancellationToken cancellationToken = default);

    Task SwitchToWindowsAsync(CancellationToken cancellationToken = default);

    Task SwitchToWslAsync(CancellationToken cancellationToken = default);

    Task RefreshDevicesAsync(CancellationToken cancellationToken = default);

    Task SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default);

    Task ForgetAsync(DeviceEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Exit path: stops monitoring and releases every device to Windows.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

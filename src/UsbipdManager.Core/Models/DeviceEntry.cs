namespace UsbipdManager.Core.Models;

/// <summary>
/// A row of the device list: a connected device, a remembered managed device that is disconnected, or both merged.
/// </summary>
public sealed record DeviceEntry(
    string? BusId,
    string DeviceKey,
    string Description,
    string? VidPid,
    UsbDeviceState? State,
    bool IsConnected,
    bool IsManaged,
    bool IsInputLike);

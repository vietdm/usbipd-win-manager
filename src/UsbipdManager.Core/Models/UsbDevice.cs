namespace UsbipdManager.Core.Models;

/// <summary>
/// One device as reported by <c>usbipd state</c>.
/// <see cref="BusId"/> is null for a device that usbipd remembers (bound) but that is not plugged in.
/// </summary>
public sealed record UsbDevice(
    string? BusId,
    string InstanceId,
    string DeviceKey,
    string Description,
    string? VidPid,
    UsbDeviceState State,
    string? ClientIpAddress)
{
    public bool IsConnected => BusId is not null;
}

using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Services;

internal static class DeviceText
{
    public static string State(UsbDeviceState state) => state switch
    {
        UsbDeviceState.NotShared => "Not shared",
        UsbDeviceState.Shared => "Shared",
        UsbDeviceState.Attached => "Attached",
        _ => state.ToString(),
    };

    /// <summary>One console line, e.g. <c>1-4  18d1:4ee7  Pixel 8  [Attached]</c>.</summary>
    public static string Line(UsbDevice device) =>
        $"{device.BusId ?? "-"}  {device.VidPid ?? "????:????"}  {device.Description}  [{State(device.State)}]";

    public static string Name(string description, string? busId) =>
        busId is null ? description : $"{description} (port {busId})";

    public static string Reason(OperationResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? "unknown error" : result.Error.Trim();
}

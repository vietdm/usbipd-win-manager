using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

/// <summary>Parses the JSON printed by <c>usbipd state</c>.</summary>
public static class UsbipdStateParser
{
    public static IReadOnlyList<UsbDevice> Parse(string json) => throw new NotImplementedException();
}

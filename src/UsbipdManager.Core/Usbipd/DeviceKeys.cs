namespace UsbipdManager.Core.Usbipd;

/// <summary>Device identity rules, see section 6.4 of docs/PLAN.md.</summary>
public static class DeviceKeys
{
    /// <summary>
    /// <c>VID_xxxx\SERIAL</c> when the instance ID carries a real serial number (the PID is ignored because Android changes it with the USB mode),
    /// otherwise the full upper-cased instance ID.
    /// </summary>
    public static string FromInstanceId(string instanceId) => throw new NotImplementedException();

    /// <summary>Lower-case <c>vvvv:pppp</c>, or null when the instance ID has no VID/PID.</summary>
    public static string? VidPidFromInstanceId(string instanceId) => throw new NotImplementedException();
}

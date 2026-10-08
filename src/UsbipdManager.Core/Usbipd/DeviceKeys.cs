using System.Text.RegularExpressions;

namespace UsbipdManager.Core.Usbipd;

/// <summary>Device identity rules, see section 6.4 of docs/PLAN.md.</summary>
public static partial class DeviceKeys
{
    /// <summary>
    /// <c>VID_xxxx\SERIAL</c> when the instance ID carries a real serial number (the PID is ignored because Android changes it with the USB mode),
    /// otherwise the full upper-cased instance ID.
    /// </summary>
    public static string FromInstanceId(string instanceId)
    {
        var normalized = instanceId.Trim().ToUpperInvariant();

        // USB\VID_18D1&PID_4EE7\35201FDH2000Q5. Windows generates the last segment from the port when the device
        // has no serial number, and generated segments always contain '&' (e.g. 5&2B5D4A0&0&4).
        var segments = normalized.Split('\\');
        if (segments.Length != 3)
        {
            return normalized;
        }

        var vid = VidRegex().Match(segments[1]);
        var serial = segments[2];
        if (!vid.Success || serial.Length == 0 || serial.Contains('&'))
        {
            return normalized;
        }

        return $"VID_{vid.Groups[1].Value}\\{serial}";
    }

    /// <summary>Lower-case <c>vvvv:pppp</c>, or null when the instance ID has no VID/PID.</summary>
    public static string? VidPidFromInstanceId(string instanceId)
    {
        var match = VidPidRegex().Match(instanceId);
        return match.Success
            ? $"{match.Groups[1].Value}:{match.Groups[2].Value}".ToLowerInvariant()
            : null;
    }

    [GeneratedRegex(@"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidRegex();

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPidRegex();
}

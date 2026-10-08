using System.Text.RegularExpressions;

namespace UsbipdManager.Core.Usbipd;

public static partial class DeviceClassifier
{
    /// <summary>
    /// True for devices that would leave Windows without input or connectivity if moved to WSL
    /// (keyboard, mouse, touchpad, HID, Bluetooth). The UI asks for confirmation before managing them.
    /// </summary>
    public static bool IsInputLike(string description) =>
        !string.IsNullOrWhiteSpace(description) && InputLikeRegex().IsMatch(description);

    // "HID" and "pen" need word boundaries on both sides so that e.g. "Hidden" or "OpenOCD" do not match.
    [GeneratedRegex(
        @"\b(keyboard|mouse|mice|touchpad|trackpad|touch\s?screen|input device|bluetooth|fingerprint|digitizer)|\b(hid|pen)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InputLikeRegex();
}

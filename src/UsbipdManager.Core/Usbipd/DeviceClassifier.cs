namespace UsbipdManager.Core.Usbipd;

public static class DeviceClassifier
{
    /// <summary>
    /// True for devices that would leave Windows without input or connectivity if moved to WSL
    /// (keyboard, mouse, touchpad, HID, Bluetooth). The UI asks for confirmation before managing them.
    /// </summary>
    public static bool IsInputLike(string description) => throw new NotImplementedException();
}

using UsbipdManager.Core.Usbipd;

namespace UsbipdManager.Tests.Usbipd;

public sealed class DeviceClassifierTests
{
    [Theory]
    [InlineData("USB Input Device")]
    [InlineData("HID Keyboard Device")]
    [InlineData("Logitech USB Optical Mouse")]
    [InlineData("Precision Touchpad")]
    [InlineData("Magic Trackpad")]
    [InlineData("HID-compliant touch screen")]
    [InlineData("Intel(R) Wireless Bluetooth(R)")]
    [InlineData("Synaptics FP Sensors (Fingerprint)")]
    [InlineData("Wacom Pen and Touch")]
    [InlineData("USB Digitizer")]
    [InlineData("hid device")]
    public void IsInputLike_True(string description)
    {
        Assert.True(DeviceClassifier.IsInputLike(description));
    }

    [Theory]
    [InlineData("Pixel 8")]
    [InlineData("USB Mass Storage Device")]
    [InlineData("Hidden Camera")]
    [InlineData("OpenOCD JTAG")]
    [InlineData("USB Serial Device (COM3)")]
    [InlineData("")]
    public void IsInputLike_False(string description)
    {
        Assert.False(DeviceClassifier.IsInputLike(description));
    }
}

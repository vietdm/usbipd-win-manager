using UsbipdManager.Core.Usbipd;

namespace UsbipdManager.Tests.Usbipd;

public sealed class DeviceKeysTests
{
    [Theory]
    [InlineData(@"USB\VID_18D1&PID_4EE7\35201FDH2000Q5", @"VID_18D1\35201FDH2000Q5")]
    [InlineData(@"usb\vid_18d1&pid_4ee7\35201fdh2000q5", @"VID_18D1\35201FDH2000Q5")]
    public void FromInstanceId_WithSerial_UsesVidAndSerial(string instanceId, string expected)
    {
        Assert.Equal(expected, DeviceKeys.FromInstanceId(instanceId));
    }

    [Fact]
    public void FromInstanceId_IgnoresPid_SoAndroidModesShareTheKey()
    {
        var mtp = DeviceKeys.FromInstanceId(@"USB\VID_18D1&PID_4EE1\35201FDH2000Q5");
        var adb = DeviceKeys.FromInstanceId(@"USB\VID_18D1&PID_4EE7\35201FDH2000Q5");

        Assert.Equal(mtp, adb);
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_C52B\5&2B5D4A0&0&4", @"USB\VID_046D&PID_C52B\5&2B5D4A0&0&4")]
    [InlineData(@"usb\vid_046d&pid_c52b\5&2b5d4a0&0&4", @"USB\VID_046D&PID_C52B\5&2B5D4A0&0&4")]
    public void FromInstanceId_WithGeneratedSegment_UsesFullInstanceId(string instanceId, string expected)
    {
        Assert.Equal(expected, DeviceKeys.FromInstanceId(instanceId));
    }

    [Theory]
    [InlineData(@"ROOT\SOMETHING\0000")]
    [InlineData(@"USB\ROOT_HUB30\4&1234")]
    [InlineData("weird")]
    public void FromInstanceId_WithoutVid_UsesFullUpperCasedInstanceId(string instanceId)
    {
        Assert.Equal(instanceId.ToUpperInvariant(), DeviceKeys.FromInstanceId(instanceId));
    }

    [Theory]
    [InlineData(@"USB\VID_18D1&PID_4EE7\35201FDH2000Q5", "18d1:4ee7")]
    [InlineData(@"usb\vid_046d&pid_c52b\5&2b5d4a0&0&4", "046d:c52b")]
    [InlineData(@"ROOT\SOMETHING\0000", null)]
    [InlineData(@"USB\VID_18D1\X", null)]
    public void VidPidFromInstanceId(string instanceId, string? expected)
    {
        Assert.Equal(expected, DeviceKeys.VidPidFromInstanceId(instanceId));
    }
}

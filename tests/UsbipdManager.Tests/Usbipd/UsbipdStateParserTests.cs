using UsbipdManager.Core.Models;
using UsbipdManager.Core.Usbipd;

namespace UsbipdManager.Tests.Usbipd;

public sealed class UsbipdStateParserTests
{
    [Fact]
    public void Parse_AttachedDevice()
    {
        const string json = """
            {"Devices":[{"BusId":"1-4","ClientIPAddress":"172.20.1.2","Description":"Pixel 8","InstanceId":"USB\\VID_18D1&PID_4EE7\\35201FDH2000Q5","IsForced":false,"PersistedGuid":"d1b6a0c2-0000-0000-0000-000000000000","StubInstanceId":"USB\\Stub"}]}
            """;

        var device = Assert.Single(UsbipdStateParser.Parse(json));

        Assert.Equal("1-4", device.BusId);
        Assert.Equal(UsbDeviceState.Attached, device.State);
        Assert.Equal("172.20.1.2", device.ClientIpAddress);
        Assert.Equal("Pixel 8", device.Description);
        Assert.Equal(@"USB\VID_18D1&PID_4EE7\35201FDH2000Q5", device.InstanceId);
        Assert.Equal(@"VID_18D1\35201FDH2000Q5", device.DeviceKey);
        Assert.Equal("18d1:4ee7", device.VidPid);
        Assert.True(device.IsConnected);
    }

    [Fact]
    public void Parse_SharedDevice()
    {
        const string json = """
            {"Devices":[{"BusId":"1-4","ClientIPAddress":null,"Description":"Pixel 8","InstanceId":"USB\\VID_18D1&PID_4EE7\\35201FDH2000Q5","IsForced":false,"PersistedGuid":"d1b6","StubInstanceId":null}]}
            """;

        var device = Assert.Single(UsbipdStateParser.Parse(json));

        Assert.Equal(UsbDeviceState.Shared, device.State);
        Assert.Null(device.ClientIpAddress);
    }

    [Fact]
    public void Parse_NotSharedDevice()
    {
        const string json = """
            {"Devices":[{"BusId":"2-1","ClientIPAddress":null,"Description":"USB Input Device","InstanceId":"USB\\VID_046D&PID_C52B\\5&2B5D4A0&0&4","IsForced":false,"PersistedGuid":null,"StubInstanceId":null}]}
            """;

        var device = Assert.Single(UsbipdStateParser.Parse(json));

        Assert.Equal(UsbDeviceState.NotShared, device.State);
        Assert.Equal(@"USB\VID_046D&PID_C52B\5&2B5D4A0&0&4", device.DeviceKey);
        Assert.Equal("046d:c52b", device.VidPid);
    }

    [Fact]
    public void Parse_PersistedDeviceThatIsNotPluggedIn()
    {
        const string json = """
            {"Devices":[{"BusId":null,"ClientIPAddress":null,"Description":"Pixel 8","InstanceId":"USB\\VID_18D1&PID_4EE7\\35201FDH2000Q5","IsForced":false,"PersistedGuid":"d1b6","StubInstanceId":null}]}
            """;

        var device = Assert.Single(UsbipdStateParser.Parse(json));

        Assert.Null(device.BusId);
        Assert.False(device.IsConnected);
        Assert.Equal(UsbDeviceState.Shared, device.State);
    }

    [Fact]
    public void Parse_ToleratesUnknownAndMissingFieldsAndCasing()
    {
        const string json = """
            {"devices":[{"busid":"3-2","instanceid":"USB\\VID_1234&PID_5678\\ABC","NewField":{"x":1}},{"Description":"no instance id"}],"Extra":42}
            """;

        var device = Assert.Single(UsbipdStateParser.Parse(json));

        Assert.Equal("3-2", device.BusId);
        Assert.Equal(UsbDeviceState.NotShared, device.State);
        Assert.False(string.IsNullOrWhiteSpace(device.Description));
    }

    [Theory]
    [InlineData("""{"Devices":[]}""")]
    [InlineData("{}")]
    [InlineData("""{"Devices":null}""")]
    public void Parse_EmptyDeviceList(string json)
    {
        Assert.Empty(UsbipdStateParser.Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("usbipd: error: Access denied.")]
    [InlineData("""{"Devices":[""")]
    public void Parse_InvalidJson_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => UsbipdStateParser.Parse(json));
    }
}

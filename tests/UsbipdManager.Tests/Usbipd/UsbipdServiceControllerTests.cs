using UsbipdManager.Core.Models;
using UsbipdManager.Core.Usbipd;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Usbipd;

public sealed class UsbipdServiceControllerTests
{
    private readonly FakeProcessRunner _runner = new();
    private readonly TestLog _log = new();

    [Fact]
    public async Task SetAutomaticStart_RunsScConfig()
    {
        var controller = new UsbipdServiceController(_runner, _log);

        var result = await controller.SetAutomaticStartAsync();

        Assert.True(result.Success);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "sc.exe"), call.FileName);
        Assert.Equal(["config", "usbipd", "start=", "auto"], call.Arguments);
    }

    [Fact]
    public async Task SetAutomaticStart_Failure_ReturnsLastLine()
    {
        _runner.Handler = _ => new ProcessResult(5, "[SC] ChangeServiceConfig FAILED 5:\r\n\r\nAccess is denied.\r\n\r\n", string.Empty);
        var controller = new UsbipdServiceController(_runner, _log);

        var result = await controller.SetAutomaticStartAsync();

        Assert.Equal(OperationResult.Fail("Access is denied."), result);
    }

    [Fact]
    public void GetInfo_UnknownServiceState_IsConsistent()
    {
        // usbipd-win is not necessarily installed on the test machine; the call must not throw either way.
        var info = new UsbipdServiceController(_runner, _log).GetInfo();

        if (!info.Exists)
        {
            Assert.Equal(UsbipdServiceInfo.NotFound, info);
        }
    }

    [Fact]
    public async Task Start_WhenServiceIsMissing_Fails()
    {
        var controller = new UsbipdServiceController(_runner, _log);
        if (controller.GetInfo().Exists)
        {
            return;
        }

        var result = await controller.StartAsync();

        Assert.Equal(OperationResult.Fail("The usbipd service is not installed."), result);
    }
}

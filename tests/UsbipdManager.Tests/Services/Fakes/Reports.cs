using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public static class Reports
{
    public static EnvironmentReport Supported() => new(
        IsElevated: true,
        UsbipdInstalled: true,
        UsbipdVersion: "5.3.0",
        UsbipdService: new UsbipdServiceInfo(true, true, ServiceStartupType.Automatic),
        WslInstalled: true,
        Distros: [new WslDistro("Ubuntu", true, 2, true)],
        Devices: []);

    public static EnvironmentReport NoWsl() => Supported() with { WslInstalled = false, Distros = [] };

    public static EnvironmentReport UsbipdMissing() => Supported() with
    {
        UsbipdInstalled = false,
        UsbipdVersion = null,
        UsbipdService = UsbipdServiceInfo.NotFound,
    };
}

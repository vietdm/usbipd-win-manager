namespace UsbipdManager.Core.Models;

public sealed record EnvironmentReport(
    bool IsElevated,
    bool UsbipdInstalled,
    string? UsbipdVersion,
    UsbipdServiceInfo UsbipdService,
    bool WslInstalled,
    IReadOnlyList<WslDistro> Distros,
    IReadOnlyList<UsbDevice> Devices)
{
    public bool HasWsl2Distro => Distros.Any(d => d.Version == 2);

    /// <summary>False means the machine cannot use the app at all (no WSL or no WSL2 distro).</summary>
    public bool IsSupported => WslInstalled && HasWsl2Distro;

    public bool IsUsbipdReady => UsbipdInstalled && UsbipdService.IsRunning;

    public bool CanSwitch => IsSupported && IsUsbipdReady;
}

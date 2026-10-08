namespace UsbipdManager.Core.Models;

public sealed record UsbipdServiceInfo(bool Exists, bool IsRunning, ServiceStartupType StartupType)
{
    public static UsbipdServiceInfo NotFound { get; } = new(false, false, ServiceStartupType.Unknown);
}

namespace UsbipdManager.Core.Models;

public sealed record WslDistro(string Name, bool IsRunning, int Version, bool IsDefault);

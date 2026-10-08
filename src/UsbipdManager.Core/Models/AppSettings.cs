namespace UsbipdManager.Core.Models;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>The mode the user last chose. Exit returns devices to Windows but keeps this value.</summary>
    public UsbMode Mode { get; set; } = UsbMode.Windows;

    public bool RestoreLastModeOnStartup { get; set; } = true;

    public bool AutoReattach { get; set; } = true;

    /// <summary>WSL distribution kept alive while in WSL2 mode; null means the default distribution.</summary>
    public string? WslDistribution { get; set; }

    public bool TrayNotifications { get; set; } = true;

    public List<ManagedDevice> ManagedDevices { get; set; } = [];
}

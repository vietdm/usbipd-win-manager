namespace UsbipdManager.Core.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,

    /// <summary>An external command that was executed, e.g. <c>usbipd attach --wsl --busid 1-4</c>.</summary>
    Command,
}

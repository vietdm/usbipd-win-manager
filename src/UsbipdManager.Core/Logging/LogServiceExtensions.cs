using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Logging;

public static class LogServiceExtensions
{
    public static void Info(this ILogService log, string message) => log.Log(LogLevel.Info, message);

    public static void Success(this ILogService log, string message) => log.Log(LogLevel.Success, message);

    public static void Warning(this ILogService log, string message) => log.Log(LogLevel.Warning, message);

    public static void Error(this ILogService log, string message) => log.Log(LogLevel.Error, message);

    public static void Command(this ILogService log, string message) => log.Log(LogLevel.Command, message);
}

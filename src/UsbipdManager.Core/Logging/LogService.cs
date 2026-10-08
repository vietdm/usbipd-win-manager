using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Logging;

public sealed class LogService : ILogService
{
    /// <param name="logDirectory">Null means <c>%LocalAppData%\UsbipdManager\logs</c>.</param>
    public LogService(string? logDirectory = null)
    {
        throw new NotImplementedException();
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public event EventHandler? Cleared;

    public string LogDirectory => throw new NotImplementedException();

    public IReadOnlyList<LogEntry> GetEntries() => throw new NotImplementedException();

    public void Log(LogLevel level, string message) => throw new NotImplementedException();

    public void Clear() => throw new NotImplementedException();
}

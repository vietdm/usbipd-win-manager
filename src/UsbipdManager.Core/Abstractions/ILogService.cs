using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>Thread-safe. <see cref="EntryAdded"/> may be raised on any thread; UI subscribers must marshal.</summary>
public interface ILogService
{
    event EventHandler<LogEntry>? EntryAdded;

    event EventHandler? Cleared;

    string LogDirectory { get; }

    IReadOnlyList<LogEntry> GetEntries();

    void Log(LogLevel level, string message);

    /// <summary>Clears the in-memory feed shown in the console; the log files are kept.</summary>
    void Clear();
}

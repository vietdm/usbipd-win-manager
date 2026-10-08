using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.TestDoubles;

public sealed class TestLog : ILogService
{
    private readonly List<LogEntry> _entries = [];

    public event EventHandler<LogEntry>? EntryAdded;

    public event EventHandler? Cleared;

    public string LogDirectory => Path.GetTempPath();

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_entries)
        {
            return [.. _entries];
        }
    }

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_entries)
        {
            _entries.Add(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public bool Contains(LogLevel level, string fragment) =>
        GetEntries().Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}

using System.Globalization;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Logging;

public sealed class LogService : ILogService
{
    internal const int MaxEntries = 2000;
    internal const string FilePrefix = "usbipd-manager-";
    internal const string FileExtension = ".log";
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly object _entriesLock = new();
    private readonly object _fileLock = new();
    private readonly Queue<LogEntry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;

    /// <param name="logDirectory">Null means <c>%LocalAppData%\UsbipdManager\logs</c>.</param>
    public LogService(string? logDirectory = null)
        : this(logDirectory, () => DateTimeOffset.Now)
    {
    }

    internal LogService(string? logDirectory, Func<DateTimeOffset> clock)
    {
        _clock = clock;
        LogDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbipdManager",
            "logs");
        DeleteOldFiles();
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public event EventHandler? Cleared;

    public string LogDirectory { get; }

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_entriesLock)
        {
            return [.. _entries];
        }
    }

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(_clock(), level, message ?? string.Empty);
        lock (_entriesLock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }

        WriteToFile(entry);
        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_entriesLock)
        {
            _entries.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    internal static string FileNameFor(DateTimeOffset timestamp) =>
        FilePrefix + timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension;

    internal static string FormatLine(LogEntry entry) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level.ToString().ToUpperInvariant(),-7}] {entry.Message}");

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(Path.Combine(LogDirectory, FileNameFor(entry.Timestamp)), FormatLine(entry) + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // The console feed keeps working when the log folder is not writable.
        }
    }

    private void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            var cutoff = _clock().Date - Retention;
            foreach (var file in Directory.EnumerateFiles(LogDirectory, FilePrefix + "*" + FileExtension))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];
                if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff)
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception)
        {
            // Cleanup is best effort.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // A locked file is retried at the next start.
        }
    }
}

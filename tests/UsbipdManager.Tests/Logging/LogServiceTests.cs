using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Logging;

public sealed class LogServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 5, 9, 123, TimeSpan.FromHours(7));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "usbipd-manager-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private LogService Create() => new(_directory, () => Now);

    [Fact]
    public void Log_adds_entries_and_raises_EntryAdded()
    {
        var log = Create();
        var raised = new List<LogEntry>();
        log.EntryAdded += (_, e) => raised.Add(e);

        log.Success("usbipd service is running.");

        var entry = Assert.Single(log.GetEntries());
        Assert.Equal(LogLevel.Success, entry.Level);
        Assert.Equal("usbipd service is running.", entry.Message);
        Assert.Equal(Now, entry.Timestamp);
        Assert.Equal([entry], raised);
    }

    [Fact]
    public void Memory_feed_is_capped_and_keeps_the_newest_entries()
    {
        var log = Create();

        for (var i = 0; i < LogService.MaxEntries + 50; i++)
        {
            log.Info($"line {i}");
        }

        var entries = log.GetEntries();
        Assert.Equal(LogService.MaxEntries, entries.Count);
        Assert.Equal("line 50", entries[0].Message);
        Assert.Equal($"line {LogService.MaxEntries + 49}", entries[^1].Message);
    }

    [Fact]
    public void Clear_empties_the_feed_and_raises_Cleared_but_keeps_the_file()
    {
        var log = Create();
        var cleared = 0;
        log.Cleared += (_, _) => cleared++;
        log.Info("hello");

        log.Clear();

        Assert.Empty(log.GetEntries());
        Assert.Equal(1, cleared);
        Assert.True(File.Exists(Path.Combine(_directory, "usbipd-manager-20261008.log")));
    }

    [Fact]
    public void Entries_are_written_to_the_daily_file_with_the_line_format()
    {
        var log = Create();

        log.Info("Checking environment...");
        log.Warning("usbipd service is stopped. Press Init to start it.");

        var lines = File.ReadAllLines(Path.Combine(_directory, "usbipd-manager-20261008.log"));
        Assert.Equal(
            [
                "2026-10-08 14:05:09.123 [INFO   ] Checking environment...",
                "2026-10-08 14:05:09.123 [WARNING] usbipd service is stopped. Press Init to start it.",
            ],
            lines);
    }

    [Fact]
    public void Files_older_than_14_days_are_deleted_at_construction()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "usbipd-manager-20260920.log");
        var kept = Path.Combine(_directory, "usbipd-manager-20260925.log");
        var other = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(old, "old");
        File.WriteAllText(kept, "kept");
        File.WriteAllText(other, "other");

        _ = Create();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void File_errors_never_throw()
    {
        // A file where the log directory should be makes every write fail.
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "not a directory");
        try
        {
            var log = Create();

            log.Error("still works");

            Assert.Single(log.GetEntries());
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public async Task Logging_from_many_threads_is_safe()
    {
        var log = Create();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                log.Info($"{t}-{i}");
            }
        })));

        Assert.Equal(800, log.GetEntries().Count);
        Assert.Equal(800, File.ReadAllLines(Path.Combine(_directory, "usbipd-manager-20261008.log")).Length);
    }
}

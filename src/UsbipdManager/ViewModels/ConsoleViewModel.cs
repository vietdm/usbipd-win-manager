using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;
using UsbipdManager.Mvvm;

namespace UsbipdManager.ViewModels;

public sealed record LogLine(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public string Time => Timestamp.LocalDateTime.ToString("HH:mm:ss");

    /// <summary>Text tag shown next to the color, so the level never relies on color alone.</summary>
    public string Tag => Level switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Command => "$",
        _ => "INFO",
    };

    public override string ToString() =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} {Tag,-5} {Message}";
}

/// <summary>
/// Feeds the console panel from <see cref="ILogService"/>. Log events arrive on any thread; they are queued and
/// flushed to the UI thread in batches so a burst of command output costs one dispatcher round trip.
/// </summary>
public sealed class ConsoleViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 5000;
    private const int TrimBatch = 500;

    private readonly ILogService _log;
    private readonly Dispatcher _dispatcher;
    private readonly object _sync = new();
    private readonly List<LogEntry> _pending = [];
    private HashSet<LogEntry>? _initialSnapshot;
    private bool _clearPending;
    private bool _flushScheduled;

    public ConsoleViewModel(ILogService log, Dispatcher dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;

        CopyCommand = new RelayCommand(CopyAll, () => Lines.Count > 0);
        ClearCommand = new RelayCommand(() => _log.Clear(), () => Lines.Count > 0);

        // Subscribe before taking the snapshot; entries raised in between are de-duplicated on the first flush.
        _log.EntryAdded += OnEntryAdded;
        _log.Cleared += OnCleared;
        var snapshot = _log.GetEntries();
        _initialSnapshot = new HashSet<LogEntry>(snapshot);
        foreach (var entry in snapshot.Skip(Math.Max(0, snapshot.Count - MaxLines)))
        {
            Lines.Add(new LogLine(entry.Timestamp, entry.Level, entry.Message));
        }

        Lines.CollectionChanged += (_, _) =>
        {
            CopyCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        };
    }

    public ObservableCollection<LogLine> Lines { get; } = [];

    public RelayCommand CopyCommand { get; }

    public RelayCommand ClearCommand { get; }

    public string GetText(IEnumerable<LogLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line.ToString());
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        _log.EntryAdded -= OnEntryAdded;
        _log.Cleared -= OnCleared;
    }

    private void CopyAll()
    {
        if (Lines.Count > 0)
        {
            TrySetClipboard(GetText(Lines));
        }
    }

    public static void TrySetClipboard(string text)
    {
        // The clipboard can be locked by another process for a moment.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        lock (_sync)
        {
            _pending.Add(entry);
            ScheduleFlush();
        }
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            _pending.Clear();
            _clearPending = true;
            _initialSnapshot = null;
            ScheduleFlush();
        }
    }

    // Caller holds _sync.
    private void ScheduleFlush()
    {
        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, Flush);
    }

    private void Flush()
    {
        LogEntry[] batch;
        bool clear;
        HashSet<LogEntry>? snapshot;
        lock (_sync)
        {
            batch = [.. _pending];
            _pending.Clear();
            clear = _clearPending;
            _clearPending = false;
            snapshot = _initialSnapshot;
            _initialSnapshot = null;
            _flushScheduled = false;
        }

        if (clear)
        {
            Lines.Clear();
        }

        foreach (var entry in batch)
        {
            if (snapshot is not null && snapshot.Contains(entry))
            {
                continue;
            }

            Lines.Add(new LogLine(entry.Timestamp, entry.Level, entry.Message));
        }

        if (Lines.Count > MaxLines + TrimBatch)
        {
            for (var i = 0; i < TrimBatch; i++)
            {
                Lines.RemoveAt(0);
            }
        }
    }
}

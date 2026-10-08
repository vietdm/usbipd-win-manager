namespace UsbipdManager.Core.Models;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

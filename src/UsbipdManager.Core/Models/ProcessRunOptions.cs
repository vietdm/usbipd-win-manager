using System.Text;

namespace UsbipdManager.Core.Models;

public sealed record ProcessRunOptions
{
    public static ProcessRunOptions Default { get; } = new();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Write the command line to the log at <see cref="LogLevel.Command"/>.</summary>
    public bool LogCommand { get; init; } = true;

    public Encoding? OutputEncoding { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}

using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.TestDoubles;

/// <summary>Records every call; <see cref="Handler"/> decides the result (default: exit code 0, empty output).</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public sealed record Call(string FileName, IReadOnlyList<string> Arguments, ProcessRunOptions? Options)
    {
        public string CommandLine => $"{Path.GetFileName(FileName)} {string.Join(' ', Arguments)}".Trim();
    }

    private readonly List<Call> _calls = [];

    public Func<Call, ProcessResult> Handler { get; set; } = _ => new ProcessResult(0, string.Empty, string.Empty);

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public List<FakeManagedProcess> StartedProcesses { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var call = new Call(fileName, [.. arguments], options);
        lock (_calls)
        {
            _calls.Add(call);
        }

        return Task.FromResult(Handler(call));
    }

    public IManagedProcess StartLongRunning(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions? options = null)
    {
        lock (_calls)
        {
            _calls.Add(new Call(fileName, [.. arguments], options));
        }

        var process = new FakeManagedProcess(StartedProcesses.Count + 1000);
        StartedProcesses.Add(process);
        return process;
    }
}

public sealed class FakeManagedProcess(int id) : IManagedProcess
{
    public int Id { get; } = id;

    public bool HasExited { get; private set; }

    public event EventHandler? Exited;

    public void SimulateExit()
    {
        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => HasExited = true;
}

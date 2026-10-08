using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeEnvironmentChecker : IEnvironmentChecker
{
    public EnvironmentReport Report { get; set; } = Reports.Supported();

    public int Calls { get; private set; }

    public Task<EnvironmentReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Report);
    }
}

/// <summary>Returns the queued reports in order, then repeats the last one.</summary>
public sealed class SequenceEnvironmentChecker(params EnvironmentReport[] reports) : IEnvironmentChecker
{
    private int _index;

    public int Calls => _index;

    public Task<EnvironmentReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var report = reports[Math.Min(_index, reports.Length - 1)];
        _index++;
        return Task.FromResult(report);
    }
}

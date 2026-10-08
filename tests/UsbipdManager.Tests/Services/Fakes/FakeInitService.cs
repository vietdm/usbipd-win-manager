using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeInitService : IInitService
{
    public EnvironmentReport Report { get; set; } = Reports.Supported();

    public int Calls { get; private set; }

    public Task<EnvironmentReport> RunAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Report);
    }
}

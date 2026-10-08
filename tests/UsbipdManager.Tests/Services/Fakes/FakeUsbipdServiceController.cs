using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeUsbipdServiceController : IUsbipdServiceController
{
    public UsbipdServiceInfo Info { get; set; } = new(true, true, ServiceStartupType.Automatic);

    public OperationResult StartResult { get; set; } = OperationResult.Ok;

    public OperationResult SetAutomaticResult { get; set; } = OperationResult.Ok;

    public int StartCalls { get; private set; }

    public int SetAutomaticCalls { get; private set; }

    public UsbipdServiceInfo GetInfo() => Info;

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;
        if (StartResult.Success)
        {
            Info = Info with { IsRunning = true };
        }

        return Task.FromResult(StartResult);
    }

    public Task<OperationResult> SetAutomaticStartAsync(CancellationToken cancellationToken = default)
    {
        SetAutomaticCalls++;
        if (SetAutomaticResult.Success)
        {
            Info = Info with { StartupType = ServiceStartupType.Automatic };
        }

        return Task.FromResult(SetAutomaticResult);
    }
}

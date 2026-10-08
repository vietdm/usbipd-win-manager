using UsbipdManager.Core.Services;

namespace UsbipdManager.Tests.Services;

public sealed class OperationGateTests
{
    [Fact]
    public async Task RunAsync_returns_the_result_and_reports_busy_while_running()
    {
        var gate = new OperationGate();
        var busyStates = new List<bool>();
        gate.BusyChanged += (_, _) => busyStates.Add(gate.IsBusy);
        var observedBusy = false;

        var result = await gate.RunAsync(_ =>
        {
            observedBusy = gate.IsBusy;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.True(observedBusy);
        Assert.False(gate.IsBusy);
        Assert.Equal([true, false], busyStates);
    }

    [Fact]
    public async Task TryRunAsync_skips_when_busy_and_runs_when_free()
    {
        var gate = new OperationGate();
        var release = new TaskCompletionSource();
        var running = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 0;
        });

        var ranWhileBusy = false;
        Assert.False(await gate.TryRunAsync(_ =>
        {
            ranWhileBusy = true;
            return Task.CompletedTask;
        }));
        Assert.False(ranWhileBusy);

        release.SetResult();
        await running;
        Assert.True(await gate.TryRunAsync(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task Operations_never_interleave()
    {
        var gate = new OperationGate();
        var active = 0;
        var maxActive = 0;

        async Task<int> Work(CancellationToken _)
        {
            var now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(5);
            Interlocked.Decrement(ref active);
            return 0;
        }

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => gate.RunAsync(Work))));

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task Gate_is_released_when_the_operation_throws()
    {
        var gate = new OperationGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync<int>(_ => throw new InvalidOperationException()));

        Assert.False(gate.IsBusy);
        Assert.True(await gate.TryRunAsync(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task Waiting_for_the_gate_can_be_cancelled()
    {
        var gate = new OperationGate();
        var release = new TaskCompletionSource();
        var running = gate.RunAsync(async _ =>
        {
            await release.Task;
            return 0;
        });
        using var cts = new CancellationTokenSource();

        var waiting = gate.RunAsync(_ => Task.FromResult(1), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await running;
        Assert.False(gate.IsBusy);
    }
}

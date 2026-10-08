using System.Collections.Concurrent;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Platform;

namespace UsbipdManager.Tests.Platform;

public sealed class SingleInstanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string UniqueName() => "UsbipdManagerTest-" + Guid.NewGuid().ToString("N");

    // Mutex ownership is per thread (and re-entrant), so each "process" runs on its own thread.
    private static T OnNewThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("Thread failed.", error);
        }

        return result;
    }

    [Fact]
    public void First_instance_acquires_and_second_does_not()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        Assert.True(first.TryAcquire());
        Assert.False(OnNewThread(second.TryAcquire));
    }

    [Theory]
    [InlineData(InstanceSignal.Activate)]
    [InlineData(InstanceSignal.Exit)]
    public void Signal_reaches_the_first_instance(InstanceSignal signal)
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        var received = new BlockingCollection<InstanceSignal>();
        first.SignalReceived += (_, s) => received.Add(s);
        Assert.True(first.TryAcquire());

        using var second = new SingleInstance(name);
        Assert.False(OnNewThread(second.TryAcquire));
        second.SignalFirstInstance(signal);

        Assert.True(received.TryTake(out var value, Timeout));
        Assert.Equal(signal, value);
        Assert.False(received.TryTake(out _, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void Signal_without_a_first_instance_is_a_no_op()
    {
        using var lonely = new SingleInstance(UniqueName());

        lonely.SignalFirstInstance(InstanceSignal.Activate);
        lonely.SignalFirstInstance(InstanceSignal.Exit);
    }

    [Fact]
    public void Released_name_can_be_acquired_again()
    {
        var name = UniqueName();
        var first = new SingleInstance(name);
        Assert.True(first.TryAcquire());
        first.Dispose();
        first.Dispose();

        using var next = new SingleInstance(name);
        Assert.True(OnNewThread(next.TryAcquire));
    }

    [Fact]
    public void Abandoned_mutex_counts_as_acquired()
    {
        var name = UniqueName();
        using var crashed = new SingleInstance(name);
        Assert.True(OnNewThread(crashed.TryAcquire));

        // The owning thread has ended without releasing the mutex.
        using var next = new SingleInstance(name);
        Assert.True(OnNewThread(next.TryAcquire));
    }

    [Fact]
    public void Throwing_subscriber_does_not_stop_the_listener()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        var received = new BlockingCollection<InstanceSignal>();
        var calls = 0;
        first.SignalReceived += (_, s) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            received.Add(s);
        };
        Assert.True(first.TryAcquire());

        using var second = new SingleInstance(name);
        second.SignalFirstInstance(InstanceSignal.Activate);
        SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 1, Timeout);
        second.SignalFirstInstance(InstanceSignal.Exit);

        Assert.True(received.TryTake(out var value, Timeout));
        Assert.Equal(InstanceSignal.Exit, value);
    }
}

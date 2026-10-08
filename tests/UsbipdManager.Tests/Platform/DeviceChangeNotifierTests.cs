using UsbipdManager.Core.Models;
using UsbipdManager.Core.Platform;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Platform;

public sealed class DeviceChangeNotifierTests
{
    [Fact]
    public void Start_and_stop_are_idempotent_and_never_throw()
    {
        var log = new TestLog();
        var notifier = new DeviceChangeNotifier(log);

        notifier.Start();
        notifier.Start();
        var running = notifier.IsRunning;

        notifier.Stop();
        notifier.Stop();
        notifier.Dispose();
        notifier.Dispose();

        Assert.False(notifier.IsRunning);
        // Either WMI worked, or the documented warning was logged instead of throwing.
        Assert.True(running || log.Contains(LogLevel.Warning, "Device change events are unavailable"));
    }

    [Fact]
    public void Start_after_dispose_throws()
    {
        var notifier = new DeviceChangeNotifier(new TestLog());
        notifier.Dispose();

        Assert.Throws<ObjectDisposedException>(notifier.Start);
    }
}

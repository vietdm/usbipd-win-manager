using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeDeviceChangeNotifier : IDeviceChangeNotifier
{
    public event EventHandler? DeviceChanged;

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public bool HasSubscribers => DeviceChanged is not null;

    public void Start() => StartCalls++;

    public void Stop() => StopCalls++;

    public void Raise() => DeviceChanged?.Invoke(this, EventArgs.Empty);
}

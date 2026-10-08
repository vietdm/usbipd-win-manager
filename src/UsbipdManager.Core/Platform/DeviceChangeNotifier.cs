using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Platform;

public sealed class DeviceChangeNotifier : IDeviceChangeNotifier, IDisposable
{
    public DeviceChangeNotifier(ILogService log)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? DeviceChanged;

    public void Start() => throw new NotImplementedException();

    public void Stop() => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

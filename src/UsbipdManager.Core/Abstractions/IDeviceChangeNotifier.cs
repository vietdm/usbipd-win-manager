namespace UsbipdManager.Core.Abstractions;

/// <summary>Raised (on a background thread, possibly in bursts) whenever Windows reports a device arrival or removal.</summary>
public interface IDeviceChangeNotifier
{
    event EventHandler? DeviceChanged;

    void Start();

    void Stop();
}

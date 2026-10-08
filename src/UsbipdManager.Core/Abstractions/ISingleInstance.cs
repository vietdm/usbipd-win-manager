namespace UsbipdManager.Core.Abstractions;

public enum InstanceSignal
{
    /// <summary>Show and activate the main window (a second launch).</summary>
    Activate,

    /// <summary>Release devices and quit gracefully (<c>--exit</c>, used by the installer before upgrade/uninstall).</summary>
    Exit,
}

public interface ISingleInstance : IDisposable
{
    /// <summary>Raised on a background thread in the first instance when another process sent a signal.</summary>
    event EventHandler<InstanceSignal>? SignalReceived;

    /// <summary>True for the first instance. A later instance gets false and should call <see cref="SignalFirstInstance"/> then exit.</summary>
    bool TryAcquire();

    void SignalFirstInstance(InstanceSignal signal);
}

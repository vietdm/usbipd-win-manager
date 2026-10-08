namespace UsbipdManager.Core.Abstractions;

public interface ISingleInstance : IDisposable
{
    /// <summary>Raised on a background thread in the first instance when another instance was launched.</summary>
    event EventHandler? ActivationRequested;

    /// <summary>True for the first instance. A later instance gets false and should call <see cref="SignalFirstInstance"/> then exit.</summary>
    bool TryAcquire();

    void SignalFirstInstance();
}

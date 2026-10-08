using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Platform;

public sealed class SingleInstance : ISingleInstance
{
    public const string DefaultName = "UsbipdManager";

    public SingleInstance(string name = DefaultName)
    {
        throw new NotImplementedException();
    }

    public event EventHandler<InstanceSignal>? SignalReceived;

    public bool TryAcquire() => throw new NotImplementedException();

    public void SignalFirstInstance(InstanceSignal signal) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

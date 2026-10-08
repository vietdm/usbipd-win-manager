using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Platform;

public sealed class SingleInstance : ISingleInstance
{
    public const string DefaultName = "UsbipdManager";

    private readonly object _sync = new();
    private readonly string _mutexName;
    private readonly string _activateName;
    private readonly string _exitName;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _exitEvent;
    private ManualResetEvent? _stopEvent;
    private Thread? _listener;
    private bool _disposed;

    public SingleInstance(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\\'))
        {
            throw new ArgumentException("The instance name must not contain a backslash.", nameof(name));
        }

        _mutexName = $@"Local\{name}.Mutex";
        _activateName = $@"Local\{name}.Activate";
        _exitName = $@"Local\{name}.Exit";
    }

    public event EventHandler<InstanceSignal>? SignalReceived;

    /// <remarks>
    /// The mutex is owned by the calling thread (mutex ownership is per thread), so a second call on the same
    /// thread in the same process would succeed again; call it once per process.
    /// </remarks>
    public bool TryAcquire()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ownsMutex)
            {
                return true;
            }

            if (_mutex is null)
            {
                _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
                _ownsMutex = createdNew;
            }

            if (!_ownsMutex)
            {
                try
                {
                    _ownsMutex = _mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // The previous owner died without releasing; ownership passed to us.
                    _ownsMutex = true;
                }
            }

            if (!_ownsMutex)
            {
                return false;
            }

            StartListener();
            return true;
        }
    }

    public void SignalFirstInstance(InstanceSignal signal)
    {
        var name = signal switch
        {
            InstanceSignal.Activate => _activateName,
            InstanceSignal.Exit => _exitName,
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
        };

        try
        {
            if (EventWaitHandle.TryOpenExisting(name, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Treat an instance we may not talk to (e.g. another integrity level) like no instance.
        }
    }

    public void Dispose()
    {
        Thread? listener;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            listener = _listener;
            _stopEvent?.Set();
        }

        if (listener is not null && listener != Thread.CurrentThread)
        {
            listener.Join(TimeSpan.FromSeconds(2));
        }

        lock (_sync)
        {
            _activateEvent?.Dispose();
            _exitEvent?.Dispose();
            _stopEvent?.Dispose();
            _activateEvent = null;
            _exitEvent = null;
            _stopEvent = null;
            _listener = null;

            if (_mutex is not null)
            {
                if (_ownsMutex)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Disposed from a thread that does not own the mutex; closing the handle below is enough.
                    }
                }

                _mutex.Dispose();
                _mutex = null;
                _ownsMutex = false;
            }
        }
    }

    private void StartListener()
    {
        if (_listener is not null)
        {
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activateName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _exitName);
        _stopEvent = new ManualResetEvent(false);

        WaitHandle[] handles = [_activateEvent, _exitEvent, _stopEvent];
        _listener = new Thread(() => Listen(handles))
        {
            IsBackground = true,
            Name = "SingleInstance signal listener",
        };
        _listener.Start();
    }

    private void Listen(WaitHandle[] handles)
    {
        while (true)
        {
            int index;
            try
            {
                index = WaitHandle.WaitAny(handles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            InstanceSignal signal;
            switch (index)
            {
                case 0:
                    signal = InstanceSignal.Activate;
                    break;
                case 1:
                    signal = InstanceSignal.Exit;
                    break;
                default:
                    return;
            }

            try
            {
                SignalReceived?.Invoke(this, signal);
            }
            catch (Exception)
            {
                // A failing subscriber must not kill the listener (or the process, since this is a background thread).
            }
        }
    }
}

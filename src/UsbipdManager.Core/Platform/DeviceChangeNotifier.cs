using System.Management;
using System.Runtime.InteropServices;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;

namespace UsbipdManager.Core.Platform;

public sealed class DeviceChangeNotifier : IDeviceChangeNotifier, IDisposable
{
    // EventType 2 = device arrival, 3 = device removal.
    internal const string Query = "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3";

    private readonly object _sync = new();
    private readonly ILogService _log;
    private ManagementEventWatcher? _watcher;
    private bool _disposed;

    public DeviceChangeNotifier(ILogService log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>Raised on a WMI thread pool thread.</summary>
    public event EventHandler? DeviceChanged;

    internal bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _watcher is not null;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null)
            {
                return;
            }

            ManagementEventWatcher? watcher = null;
            try
            {
                watcher = new ManagementEventWatcher(new WqlEventQuery(Query));
                watcher.EventArrived += OnEventArrived;
                watcher.Start();
                _watcher = watcher;
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                if (watcher is not null)
                {
                    watcher.EventArrived -= OnEventArrived;
                    watcher.Dispose();
                }

                _log.Warning("Device change events are unavailable; devices will be refreshed periodically.");
            }
        }
    }

    public void Stop()
    {
        ManagementEventWatcher? watcher;
        lock (_sync)
        {
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.EventArrived -= OnEventArrived;
        try
        {
            watcher.Stop();
        }
        catch (Exception ex) when (ex is ManagementException or COMException or InvalidOperationException)
        {
            // Already stopped or WMI went away; nothing left to clean up but the object itself.
        }
        finally
        {
            watcher.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        e.NewEvent?.Dispose();
        try
        {
            DeviceChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // An exception escaping on the WMI thread would end the process.
            _log.Warning($"A device change could not be handled: {ex.Message}");
        }
    }
}

using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

/// <summary>Simulates usbipd: a mutable device table that bind/unbind/attach/detach change, plus a call log.</summary>
public sealed class FakeUsbipdClient : IUsbipdClient
{
    private readonly object _lock = new();
    private readonly List<UsbDevice> _devices = [];
    private readonly List<string> _calls = [];
    private readonly Dictionary<string, int> _reenumerating = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Like real usbipd: after a detach the device is missing from this many state reads (re-enumeration).</summary>
    public int MissingReadsAfterDetach { get; set; }

    /// <summary>Attach waits this long (honoring cancellation) before it completes, to simulate a hanging attach.</summary>
    public TimeSpan AttachDelay { get; set; }

    public string? ExecutablePath { get; set; } = @"C:\Program Files\usbipd-win\usbipd.exe";

    public string? Version { get; set; } = "5.3.0";

    public Exception? GetDevicesException { get; set; }

    public HashSet<string> FailBind { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> FailAttach { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> FailUnbind { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OperationResult DetachAllResult { get; set; } = OperationResult.Ok;

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Mutating calls only (bind/unbind/attach/detach), without the reads.</summary>
    public IReadOnlyList<string> Actions => Calls.Where(c => c != "state").ToList();

    public IReadOnlyList<UsbDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    public void ClearCalls()
    {
        lock (_lock)
        {
            _calls.Clear();
        }
    }

    public UsbDevice Plug(string busId, string deviceKey, string description, UsbDeviceState state = UsbDeviceState.NotShared, string? vidPid = "18d1:4ee7")
    {
        var device = new UsbDevice(busId, $@"USB\{deviceKey}", deviceKey, description, vidPid, state, null);
        lock (_lock)
        {
            _devices.RemoveAll(d => d.BusId == busId || (d.BusId is null && d.DeviceKey == deviceKey));
            _devices.Add(device);
        }

        return device;
    }

    /// <summary>Like usbipd: a bound device stays listed with no bus ID; an unbound one disappears.</summary>
    public void Unplug(string busId)
    {
        lock (_lock)
        {
            var device = _devices.Single(d => d.BusId == busId);
            _devices.Remove(device);
            if (device.State != UsbDeviceState.NotShared)
            {
                _devices.Add(device with { BusId = null, State = UsbDeviceState.Shared });
            }
        }
    }

    public string? ResolveExecutablePath() => ExecutablePath;

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Version);

    public Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        Record("state");
        if (GetDevicesException is not null)
        {
            throw GetDevicesException;
        }

        lock (_lock)
        {
            IReadOnlyList<UsbDevice> visible = _devices.Where(d => d.BusId is null || !_reenumerating.ContainsKey(d.BusId)).ToList();
            foreach (var busId in _reenumerating.Keys.ToList())
            {
                if (--_reenumerating[busId] <= 0)
                {
                    _reenumerating.Remove(busId);
                }
            }

            return Task.FromResult(visible);
        }
    }

    public Task<OperationResult> BindAsync(string busId, CancellationToken cancellationToken = default) =>
        Change($"bind {busId}", busId, FailBind, d => d.State == UsbDeviceState.NotShared ? d with { State = UsbDeviceState.Shared } : d);

    public Task<OperationResult> UnbindAsync(string busId, CancellationToken cancellationToken = default) =>
        Change($"unbind {busId}", busId, FailUnbind, d => d with { State = UsbDeviceState.NotShared });

    public async Task<OperationResult> AttachToWslAsync(string busId, CancellationToken cancellationToken = default)
    {
        if (AttachDelay > TimeSpan.Zero)
        {
            Record($"attach {busId} (waiting)");
            await Task.Delay(AttachDelay, cancellationToken);
        }

        return await Change($"attach {busId}", busId, FailAttach, d => d.State == UsbDeviceState.NotShared ? null : d with { State = UsbDeviceState.Attached });
    }

    public async Task<OperationResult> DetachAsync(string busId, CancellationToken cancellationToken = default)
    {
        var result = await Change($"detach {busId}", busId, null, d => d with { State = UsbDeviceState.Shared });
        if (result.Success && MissingReadsAfterDetach > 0)
        {
            lock (_lock)
            {
                _reenumerating[busId] = MissingReadsAfterDetach;
            }
        }

        return result;
    }

    public Task<OperationResult> DetachAllAsync(CancellationToken cancellationToken = default)
    {
        Record("detach --all");
        lock (_lock)
        {
            for (var i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].State == UsbDeviceState.Attached)
                {
                    _devices[i] = _devices[i] with { State = UsbDeviceState.Shared };
                }
            }
        }

        return Task.FromResult(DetachAllResult);
    }

    private Task<OperationResult> Change(string call, string busId, HashSet<string>? failures, Func<UsbDevice, UsbDevice?> change)
    {
        Record(call);
        if (failures?.Contains(busId) == true)
        {
            return Task.FromResult(OperationResult.Fail($"simulated {call.Split(' ')[0]} failure"));
        }

        lock (_lock)
        {
            var index = _reenumerating.ContainsKey(busId) ? -1 : _devices.FindIndex(d => d.BusId == busId);
            if (index < 0)
            {
                return Task.FromResult(OperationResult.Fail($"There is no device with busid '{busId}'."));
            }

            var changed = change(_devices[index]);
            if (changed is null)
            {
                return Task.FromResult(OperationResult.Fail("Device is not shared."));
            }

            _devices[index] = changed;
        }

        return Task.FromResult(OperationResult.Ok);
    }

    private void Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}

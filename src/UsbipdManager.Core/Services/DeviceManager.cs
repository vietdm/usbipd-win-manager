using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Usbipd;

namespace UsbipdManager.Core.Services;

/// <summary>
/// Not gated: callers (the app controller) serialize calls. <see cref="Entries"/> can be read from any thread.
/// </summary>
public sealed class DeviceManager : IDeviceManager
{
    internal static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(30);

    // A device that just appeared or changed state is often still re-enumerating (usbipd detach and resets do that);
    // an attach started in that window hung for 60 s once. Automatic bind/attach waits until it has been stable this long.
    internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    // After usbipd detach the bus ID disappears for ~1-3 s while Windows re-enumerates the device; unbind fails meanwhile.
    internal static readonly TimeSpan ReenumerationPoll = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ReenumerationTimeout = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan DefaultAutoAttachTimeout = TimeSpan.FromSeconds(20);

    private const int MaxSettleWaits = 3;

    private readonly IUsbipdClient _usbipd;
    private readonly IWslClient _wsl;
    private readonly ISettingsStore _settings;
    private readonly ILogService _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, bool> _isInputLike;
    private readonly object _lock = new();

    // Last failed automatic bind/attach per device, so repeated device events do not retry (and log) every time.
    private readonly Dictionary<string, DateTimeOffset> _failures = new(StringComparer.OrdinalIgnoreCase);

    // When each connected device last appeared or changed state (see SettleTime).
    private readonly Dictionary<string, (UsbDeviceState State, DateTimeOffset Since)> _lastChange = new(StringComparer.OrdinalIgnoreCase);
    private bool _baselineTaken;

    private IReadOnlyList<DeviceEntry> _entries = [];
    private IReadOnlyList<UsbDevice> _devices = [];

    public DeviceManager(IUsbipdClient usbipd, IWslClient wsl, ISettingsStore settings, ILogService log)
        : this(usbipd, wsl, settings, log, () => DateTimeOffset.Now, DeviceClassifier.IsInputLike)
    {
    }

    internal DeviceManager(
        IUsbipdClient usbipd,
        IWslClient wsl,
        ISettingsStore settings,
        ILogService log,
        Func<DateTimeOffset> clock,
        Func<string, bool> isInputLike)
    {
        _usbipd = usbipd;
        _wsl = wsl;
        _settings = settings;
        _log = log;
        _clock = clock;
        _isInputLike = isInputLike;
    }

    public event EventHandler? EntriesChanged;

    /// <summary>Tests replace it to advance a fake clock instead of waiting.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Automatic attaches give up sooner than user-started ones, so a hanging attach does not hold the gate for long.</summary>
    internal TimeSpan AutoAttachTimeout { get; set; } = DefaultAutoAttachTimeout;

    public IReadOnlyList<DeviceEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries;
            }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        for (var waits = 0; ; waits++)
        {
            ClearFailuresOfUnpluggedDevices(devices);
            var (changed, settleWait) = await ApplyRulesAsync(devices, _settings.Current, cancellationToken).ConfigureAwait(false);
            if (changed)
            {
                devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (settleWait <= TimeSpan.Zero || waits >= MaxSettleWaits)
            {
                break;
            }

            Publish(devices);
            await Delay(settleWait, cancellationToken).ConfigureAwait(false);
            devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        }

        Publish(devices);
    }

    public async Task<OperationResult> SetManagedAsync(DeviceEntry entry, bool managed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return managed
            ? await TurnOnAsync(entry, cancellationToken).ConfigureAwait(false)
            : await TurnOffAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public void Forget(DeviceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.BusId is null)
        {
            return;
        }

        RemoveManaged(entry.BusId, entry.DeviceKey);
        _log.Info($"Forgot {DeviceText.Name(entry.Description, entry.BusId)}.");
        Publish(LastDevices());
    }

    public async Task<OperationResult> BindConnectedManagedAsync(CancellationToken cancellationToken = default)
    {
        var devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        var settings = _settings.Current;
        var failed = new List<string>();
        var changed = false;

        foreach (var device in ConnectedManaged(devices, settings).Where(d => d.State == UsbDeviceState.NotShared))
        {
            var name = DeviceText.Name(device.Description, device.BusId);
            _log.Info($"Sharing {name}...");
            var result = await _usbipd.BindAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                ClearFailure(device);
                changed = true;
            }
            else
            {
                _log.Error($"Could not share {name}: {DeviceText.Reason(result)}");
                failed.Add(device.Description);
            }
        }

        if (changed)
        {
            devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        }

        Publish(devices);
        return failed.Count == 0 ? OperationResult.Ok : OperationResult.Fail($"Could not share: {string.Join(", ", failed)}.");
    }

    public async Task<OperationResult> AttachConnectedManagedAsync(CancellationToken cancellationToken = default)
    {
        var devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        var settings = _settings.Current;
        var pending = ConnectedManaged(devices, settings).Where(d => d.State != UsbDeviceState.Attached).ToList();
        if (pending.Count == 0)
        {
            Publish(devices);
            return OperationResult.Ok;
        }

        var wsl = await _wsl.EnsureRunningAsync(settings.WslDistribution, cancellationToken).ConfigureAwait(false);
        if (!wsl.Success)
        {
            _log.Error($"Could not start WSL 2: {DeviceText.Reason(wsl)}");
            Publish(devices);
            return OperationResult.Fail($"Could not start WSL 2: {DeviceText.Reason(wsl)}");
        }

        var failed = new List<string>();
        foreach (var device in pending)
        {
            var name = DeviceText.Name(device.Description, device.BusId);
            var error = await BindAndAttachAsync(device, cancellationToken).ConfigureAwait(false);
            if (error is null)
            {
                ClearFailure(device);
                _log.Success($"{name} is attached to WSL2.");
            }
            else
            {
                _log.Error($"Could not attach {name} to WSL2: {error}");
                failed.Add(device.Description);
            }
        }

        Publish(await ReadDevicesAsync(cancellationToken).ConfigureAwait(false));
        return failed.Count == 0 ? OperationResult.Ok : OperationResult.Fail($"Could not attach: {string.Join(", ", failed)}.");
    }

    internal static bool Matches(ManagedDevice managed, string? busId, string deviceKey) =>
        busId is not null
        && string.Equals(managed.BusId, busId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(managed.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase);

    internal IReadOnlyList<DeviceEntry> BuildEntries(IReadOnlyList<UsbDevice> devices, AppSettings settings)
    {
        var connected = devices.Where(d => d.IsConnected).ToList();
        var entries = connected
            .Select(d => new DeviceEntry(
                d.BusId,
                d.DeviceKey,
                d.Description,
                d.VidPid,
                d.State,
                IsConnected: true,
                IsManaged: settings.ManagedDevices.Any(m => Matches(m, d.BusId, d.DeviceKey)),
                IsInputLike(d.Description)))
            .OrderBy(e => e.BusId, NaturalStringComparer.Instance)
            .ThenBy(e => e.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var disconnected = settings.ManagedDevices
            .Where(m => !connected.Any(d => Matches(m, d.BusId, d.DeviceKey)))
            .Select(m => new DeviceEntry(
                m.BusId,
                m.DeviceKey,
                m.Description,
                devices.FirstOrDefault(d => string.Equals(d.DeviceKey, m.DeviceKey, StringComparison.OrdinalIgnoreCase))?.VidPid,
                State: null,
                IsConnected: false,
                IsManaged: true,
                IsInputLike(m.Description)))
            .OrderBy(e => e.BusId, NaturalStringComparer.Instance)
            .ThenBy(e => e.Description, StringComparer.OrdinalIgnoreCase);

        entries.AddRange(disconnected);
        return entries;
    }

    private async Task<OperationResult> TurnOnAsync(DeviceEntry entry, CancellationToken cancellationToken)
    {
        var name = DeviceText.Name(entry.Description, entry.BusId);
        if (!entry.IsConnected || entry.BusId is null)
        {
            _log.Error($"{entry.Description} is not connected. Plug it in, then turn it on.");
            return OperationResult.Fail("The device is not connected.");
        }

        var devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        var device = FindConnected(devices, entry);
        if (device is null)
        {
            _log.Error($"{name} is no longer connected. The device list was refreshed.");
            Publish(devices);
            return OperationResult.Fail("The device is no longer connected.");
        }

        if (device.State == UsbDeviceState.NotShared)
        {
            var bind = await _usbipd.BindAsync(entry.BusId, cancellationToken).ConfigureAwait(false);
            if (!bind.Success)
            {
                _log.Error($"Could not share {name}: {DeviceText.Reason(bind)}");
                Publish(devices);
                return OperationResult.Fail(DeviceText.Reason(bind));
            }
        }

        var now = _clock();
        _settings.Update(s =>
        {
            s.ManagedDevices.RemoveAll(m => Matches(m, entry.BusId, entry.DeviceKey));
            s.ManagedDevices.Add(new ManagedDevice(entry.BusId, device.DeviceKey, device.Description, now));
        });
        ClearFailure(device);
        _log.Success($"{name} is ON. It is shared and follows the selected mode.");

        var result = OperationResult.Ok;
        var settings = _settings.Current;
        if (settings.Mode == UsbMode.Wsl && device.State != UsbDeviceState.Attached)
        {
            result = await AttachNowAsync(device, settings, cancellationToken).ConfigureAwait(false);
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<OperationResult> AttachNowAsync(UsbDevice device, AppSettings settings, CancellationToken cancellationToken)
    {
        var name = DeviceText.Name(device.Description, device.BusId);
        var wsl = await _wsl.EnsureRunningAsync(settings.WslDistribution, cancellationToken).ConfigureAwait(false);
        if (!wsl.Success)
        {
            RecordFailure(device);
            _log.Error($"Could not start WSL 2 to attach {name}: {DeviceText.Reason(wsl)}");
            return OperationResult.Fail(DeviceText.Reason(wsl));
        }

        var attach = await _usbipd.AttachToWslAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
        if (!attach.Success)
        {
            RecordFailure(device);
            _log.Error($"Could not attach {name} to WSL2: {DeviceText.Reason(attach)}");
            return OperationResult.Fail(DeviceText.Reason(attach));
        }

        _log.Success($"{name} is attached to WSL2.");
        return OperationResult.Ok;
    }

    private async Task<OperationResult> TurnOffAsync(DeviceEntry entry, CancellationToken cancellationToken)
    {
        var name = DeviceText.Name(entry.Description, entry.BusId);
        if (!entry.IsConnected || entry.BusId is null)
        {
            Forget(entry);
            return OperationResult.Ok;
        }

        var devices = await ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        var device = FindConnected(devices, entry);
        if (device is not null)
        {
            var detached = false;
            if (device.State == UsbDeviceState.Attached)
            {
                var detach = await _usbipd.DetachAsync(entry.BusId, cancellationToken).ConfigureAwait(false);
                if (!detach.Success)
                {
                    _log.Error($"Could not detach {name}: {DeviceText.Reason(detach)}");
                    Publish(devices);
                    return OperationResult.Fail(DeviceText.Reason(detach));
                }

                detached = true;
            }

            if (device.State != UsbDeviceState.NotShared)
            {
                var unbind = detached
                    ? await UnbindAfterReenumerationAsync(entry, cancellationToken).ConfigureAwait(false)
                    : await _usbipd.UnbindAsync(entry.BusId, cancellationToken).ConfigureAwait(false);
                if (!unbind.Success && detached)
                {
                    // Already back in Windows: keep it OFF anyway, otherwise auto-attach would move it to WSL2 again.
                    RemoveManaged(entry.BusId, entry.DeviceKey);
                    ClearFailure(entry.BusId, entry.DeviceKey);
                    _log.Warning($"{name} is OFF and back in Windows, but it is still shared: {DeviceText.Reason(unbind)}");
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                    return OperationResult.Ok;
                }

                if (!unbind.Success)
                {
                    _log.Error($"Could not stop sharing {name}: {DeviceText.Reason(unbind)}");
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                    return OperationResult.Fail(DeviceText.Reason(unbind));
                }
            }
        }

        RemoveManaged(entry.BusId, entry.DeviceKey);
        ClearFailure(entry.BusId, entry.DeviceKey);
        _log.Success($"{name} is OFF and back in Windows.");
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok;
    }

    // Polls until the device is listed again after a detach, then unbinds it.
    private async Task<OperationResult> UnbindAfterReenumerationAsync(DeviceEntry entry, CancellationToken cancellationToken)
    {
        var deadline = _clock() + ReenumerationTimeout;
        var last = OperationResult.Fail($"The device did not come back within {ReenumerationTimeout.TotalSeconds:0} seconds after the detach.");
        while (true)
        {
            await Delay(ReenumerationPoll, cancellationToken).ConfigureAwait(false);
            var device = FindConnected(await ReadDevicesAsync(cancellationToken).ConfigureAwait(false), entry);
            if (device is not null)
            {
                if (device.State == UsbDeviceState.NotShared)
                {
                    return OperationResult.Ok;
                }

                last = await _usbipd.UnbindAsync(entry.BusId!, cancellationToken).ConfigureAwait(false);
                if (last.Success)
                {
                    return last;
                }
            }

            if (_clock() >= deadline)
            {
                return last;
            }
        }
    }

    /// <summary>
    /// Changed: something was done and the state must be re-read. SettleWait: a managed device that needs a bind or
    /// attach is still settling (see SettleTime); the caller waits this long and applies the rules again.
    /// </summary>
    private async Task<(bool Changed, TimeSpan SettleWait)> ApplyRulesAsync(IReadOnlyList<UsbDevice> devices, AppSettings settings, CancellationToken cancellationToken)
    {
        var changed = false;
        var settleWait = TimeSpan.Zero;
        var autoAttach = settings.Mode == UsbMode.Wsl && settings.AutoReattach;
        bool? wslReady = null;

        foreach (var device in ConnectedManaged(devices, settings))
        {
            if (IsBackingOff(device))
            {
                continue;
            }

            var needsWork = device.State == UsbDeviceState.NotShared || (autoAttach && device.State != UsbDeviceState.Attached);
            var remaining = SettleRemaining(device);
            if (needsWork && remaining > TimeSpan.Zero)
            {
                settleWait = remaining > settleWait ? remaining : settleWait;
                continue;
            }

            var name = DeviceText.Name(device.Description, device.BusId);
            var state = device.State;
            if (state == UsbDeviceState.NotShared)
            {
                _log.Info($"Auto-bind: {device.Description} is back on port {device.BusId}.");
                var bind = await _usbipd.BindAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
                if (!bind.Success)
                {
                    RecordFailure(device);
                    _log.Error($"Could not share {name}: {DeviceText.Reason(bind)}");
                    continue;
                }

                changed = true;
                state = UsbDeviceState.Shared;
            }

            if (!autoAttach || state == UsbDeviceState.Attached)
            {
                continue;
            }

            if (wslReady is null)
            {
                var wsl = await _wsl.EnsureRunningAsync(settings.WslDistribution, cancellationToken).ConfigureAwait(false);
                wslReady = wsl.Success;
                if (!wsl.Success)
                {
                    _log.Error($"Could not start WSL 2 for auto-attach: {DeviceText.Reason(wsl)}");
                }
            }

            if (wslReady != true)
            {
                RecordFailure(device);
                continue;
            }

            _log.Info($"Auto-attach: {name} to WSL2.");
            var attach = await AutoAttachAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
            changed = true;
            if (attach.Success)
            {
                _log.Success($"{name} is attached to WSL2.");
            }
            else
            {
                RecordFailure(device);
                _log.Error($"Could not attach {name} to WSL2: {DeviceText.Reason(attach)}");
            }
        }

        return (changed, settleWait);
    }

    private async Task<OperationResult> AutoAttachAsync(string busId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AutoAttachTimeout);
        try
        {
            return await _usbipd.AttachToWslAsync(busId, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Fail($"usbipd did not finish within {AutoAttachTimeout.TotalSeconds:0} seconds.");
        }
    }

    private async Task<string?> BindAndAttachAsync(UsbDevice device, CancellationToken cancellationToken)
    {
        if (device.State == UsbDeviceState.NotShared)
        {
            var bind = await _usbipd.BindAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
            if (!bind.Success)
            {
                return DeviceText.Reason(bind);
            }
        }

        var attach = await _usbipd.AttachToWslAsync(device.BusId!, cancellationToken).ConfigureAwait(false);
        return attach.Success ? null : DeviceText.Reason(attach);
    }

    private async Task<IReadOnlyList<UsbDevice>> ReadDevicesAsync(CancellationToken cancellationToken)
    {
        if (_usbipd.ResolveExecutablePath() is null)
        {
            return [];
        }

        var devices = await _usbipd.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        TrackChanges(devices);
        return devices;
    }

    // The first read is the baseline (devices present at startup are not "new"); later appearances and state changes start a settle period.
    private void TrackChanges(IReadOnlyList<UsbDevice> devices)
    {
        var now = _clock();
        lock (_lock)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in devices.Where(d => d.IsConnected))
            {
                var key = FailureKey(device.BusId, device.DeviceKey);
                present.Add(key);
                if (!_lastChange.TryGetValue(key, out var last) || last.State != device.State)
                {
                    _lastChange[key] = (device.State, _baselineTaken ? now : DateTimeOffset.MinValue);
                }
            }

            foreach (var key in _lastChange.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _lastChange.Remove(key);
            }

            _baselineTaken = true;
        }
    }

    private TimeSpan SettleRemaining(UsbDevice device)
    {
        lock (_lock)
        {
            if (!_lastChange.TryGetValue(FailureKey(device.BusId, device.DeviceKey), out var last) || last.Since == DateTimeOffset.MinValue)
            {
                return TimeSpan.Zero;
            }

            var remaining = SettleTime - (_clock() - last.Since);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private static IEnumerable<UsbDevice> ConnectedManaged(IReadOnlyList<UsbDevice> devices, AppSettings settings) =>
        devices
            .Where(d => d.IsConnected && settings.ManagedDevices.Any(m => Matches(m, d.BusId, d.DeviceKey)))
            .OrderBy(d => d.BusId, NaturalStringComparer.Instance)
            .ToList();

    private static UsbDevice? FindConnected(IReadOnlyList<UsbDevice> devices, DeviceEntry entry) =>
        devices.FirstOrDefault(d =>
            d.IsConnected
            && string.Equals(d.BusId, entry.BusId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.DeviceKey, entry.DeviceKey, StringComparison.OrdinalIgnoreCase));

    private void RemoveManaged(string busId, string deviceKey) =>
        _settings.Update(s => s.ManagedDevices.RemoveAll(m => Matches(m, busId, deviceKey)));

    private bool IsInputLike(string description) => _isInputLike(description);

    private void Publish(IReadOnlyList<UsbDevice> devices)
    {
        var entries = BuildEntries(devices, _settings.Current);
        bool changed;
        lock (_lock)
        {
            _devices = devices;
            changed = !_entries.SequenceEqual(entries);
            if (changed)
            {
                _entries = entries;
            }
        }

        if (changed)
        {
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IReadOnlyList<UsbDevice> LastDevices()
    {
        lock (_lock)
        {
            return _devices;
        }
    }

    private static string FailureKey(string? busId, string deviceKey) => $"{busId}|{deviceKey}";

    private bool IsBackingOff(UsbDevice device)
    {
        lock (_lock)
        {
            return _failures.TryGetValue(FailureKey(device.BusId, device.DeviceKey), out var at) && _clock() - at < RetryBackoff;
        }
    }

    private void RecordFailure(UsbDevice device)
    {
        lock (_lock)
        {
            _failures[FailureKey(device.BusId, device.DeviceKey)] = _clock();
        }
    }

    private void ClearFailure(UsbDevice device) => ClearFailure(device.BusId, device.DeviceKey);

    private void ClearFailure(string? busId, string deviceKey)
    {
        lock (_lock)
        {
            _failures.Remove(FailureKey(busId, deviceKey));
        }
    }

    // Unplugging and re-plugging a device is a deliberate user action, so it retries immediately.
    private void ClearFailuresOfUnpluggedDevices(IReadOnlyList<UsbDevice> devices)
    {
        var present = devices.Where(d => d.IsConnected).Select(d => FailureKey(d.BusId, d.DeviceKey)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            foreach (var key in _failures.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _failures.Remove(key);
            }
        }
    }
}

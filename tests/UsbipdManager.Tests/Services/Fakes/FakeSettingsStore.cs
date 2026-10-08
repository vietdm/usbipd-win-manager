using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeSettingsStore : ISettingsStore
{
    private readonly object _lock = new();
    private AppSettings _current;

    public FakeSettingsStore(Action<AppSettings>? configure = null)
    {
        _current = new AppSettings();
        configure?.Invoke(_current);
    }

    public event EventHandler? Changed;

    public string SettingsPath => "memory://settings.json";

    public int UpdateCount { get; private set; }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return Clone(_current);
            }
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_lock)
        {
            var next = Clone(_current);
            mutate(next);
            _current = next;
            UpdateCount++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        Theme = s.Theme,
        Mode = s.Mode,
        RestoreLastModeOnStartup = s.RestoreLastModeOnStartup,
        AutoReattach = s.AutoReattach,
        WslDistribution = s.WslDistribution,
        TrayNotifications = s.TrayNotifications,
        ManagedDevices = [.. s.ManagedDevices],
    };
}

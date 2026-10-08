using UsbipdManager.Core.Models;
using UsbipdManager.Core.Settings;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "usbipd-manager-tests", Guid.NewGuid().ToString("N"));
    private readonly TestLog _log = new();

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new SettingsStore(_log, SettingsPath);

        var settings = store.Current;
        Assert.Equal(UsbMode.Windows, settings.Mode);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.True(settings.RestoreLastModeOnStartup);
        Assert.True(settings.AutoReattach);
        Assert.True(settings.TrayNotifications);
        Assert.Null(settings.WslDistribution);
        Assert.Empty(settings.ManagedDevices);
        Assert.Empty(_log.GetEntries());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Update_persists_and_a_new_store_reads_it_back()
    {
        var store = new SettingsStore(_log, SettingsPath);
        var added = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.FromHours(7));

        store.Update(s =>
        {
            s.Mode = UsbMode.Wsl;
            s.Theme = ThemeMode.Dark;
            s.WslDistribution = "Ubuntu-24.04";
            s.AutoReattach = false;
            s.ManagedDevices.Add(new ManagedDevice("1-4", @"VID_18D1\SERIAL", "Pixel 8", added));
        });

        var reloaded = new SettingsStore(_log, SettingsPath).Current;
        Assert.Equal(UsbMode.Wsl, reloaded.Mode);
        Assert.Equal(ThemeMode.Dark, reloaded.Theme);
        Assert.Equal("Ubuntu-24.04", reloaded.WslDistribution);
        Assert.False(reloaded.AutoReattach);
        Assert.Equal(new ManagedDevice("1-4", @"VID_18D1\SERIAL", "Pixel 8", added), Assert.Single(reloaded.ManagedDevices));
    }

    [Fact]
    public void File_is_indented_with_enums_as_strings_and_no_temp_file_is_left()
    {
        var store = new SettingsStore(_log, SettingsPath);

        store.Update(s => s.Mode = UsbMode.Wsl);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"Wsl\"", json);
        Assert.Contains(Environment.NewLine + "  ", json);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Reading_is_case_insensitive()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "MODE": "wsl", "Theme": "Light", "restorelastmodeonstartup": false }""");

        var settings = new SettingsStore(_log, SettingsPath).Current;

        Assert.Equal(UsbMode.Wsl, settings.Mode);
        Assert.Equal(ThemeMode.Light, settings.Theme);
        Assert.False(settings.RestoreLastModeOnStartup);
    }

    [Fact]
    public void Corrupt_file_is_renamed_logged_and_defaults_are_used()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var store = new SettingsStore(_log, SettingsPath);

        Assert.Equal(UsbMode.Windows, store.Current.Mode);
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal("{ this is not json", File.ReadAllText(SettingsPath + ".corrupt"));
        Assert.True(_log.Contains(LogLevel.Warning, "corrupt"));
    }

    [Fact]
    public void Null_managed_devices_in_the_file_become_an_empty_list()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "managedDevices": null }""");

        Assert.Empty(new SettingsStore(_log, SettingsPath).Current.ManagedDevices);
    }

    [Fact]
    public void Current_is_an_isolated_snapshot()
    {
        var store = new SettingsStore(_log, SettingsPath);

        var snapshot = store.Current;
        snapshot.Mode = UsbMode.Wsl;
        snapshot.ManagedDevices.Add(new ManagedDevice("1-1", "K", "D", DateTimeOffset.Now));

        Assert.Equal(UsbMode.Windows, store.Current.Mode);
        Assert.Empty(store.Current.ManagedDevices);
        Assert.NotSame(store.Current, store.Current);
    }

    [Fact]
    public void Update_raises_Changed_after_the_new_values_are_visible()
    {
        var store = new SettingsStore(_log, SettingsPath);
        UsbMode? seen = null;
        store.Changed += (_, _) => seen = store.Current.Mode;

        store.Update(s => s.Mode = UsbMode.Wsl);

        Assert.Equal(UsbMode.Wsl, seen);
    }

    [Fact]
    public void A_throwing_mutation_changes_nothing()
    {
        var store = new SettingsStore(_log, SettingsPath);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        Assert.Throws<InvalidOperationException>(() => store.Update(s =>
        {
            s.Mode = UsbMode.Wsl;
            throw new InvalidOperationException();
        }));

        Assert.Equal(UsbMode.Windows, store.Current.Mode);
        Assert.Equal(0, changed);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task Concurrent_updates_are_not_lost()
    {
        var store = new SettingsStore(_log, SettingsPath);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            store.Update(s => s.ManagedDevices.Add(new ManagedDevice($"1-{i}", $"K{i}", "D", DateTimeOffset.Now))))));

        Assert.Equal(20, store.Current.ManagedDevices.Count);
        Assert.Equal(20, new SettingsStore(_log, SettingsPath).Current.ManagedDevices.Count);
    }
}

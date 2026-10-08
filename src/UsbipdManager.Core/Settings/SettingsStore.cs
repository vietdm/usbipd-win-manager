using System.Text.Json;
using System.Text.Json.Serialization;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Settings;

public sealed class SettingsStore : ISettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly ILogService _log;
    private AppSettings _current;

    /// <param name="settingsPath">Null means <c>%AppData%\UsbipdManager\settings.json</c>.</param>
    public SettingsStore(ILogService log, string? settingsPath = null)
    {
        _log = log;
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsbipdManager",
            "settings.json");
        _current = Load();
    }

    public event EventHandler? Changed;

    public string SettingsPath { get; }

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
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_lock)
        {
            var next = Clone(_current);
            mutate(next);
            Normalize(next);
            Save(next);
            _current = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal static AppSettings Clone(AppSettings settings) =>
        Normalize(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new AppSettings());

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.ManagedDevices ??= [];
        settings.ManagedDevices.RemoveAll(d => d is null || string.IsNullOrWhiteSpace(d.BusId) || string.IsNullOrWhiteSpace(d.DeviceKey));
        return settings;
    }

    private AppSettings Load()
    {
        string json;
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            json = File.ReadAllText(SettingsPath);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read settings ({ex.Message}). Default settings are used.");
            return new AppSettings();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? throw new JsonException("The file is empty."));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            var backup = SettingsPath + ".corrupt";
            try
            {
                File.Move(SettingsPath, backup, overwrite: true);
                _log.Warning($"Settings file is corrupt and was renamed to {Path.GetFileName(backup)}. Default settings are used.");
            }
            catch (Exception moveEx)
            {
                _log.Warning($"Settings file is corrupt and could not be renamed ({moveEx.Message}). Default settings are used.");
            }

            return new AppSettings();
        }
    }

    private void Save(AppSettings settings)
    {
        var temp = SettingsPath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // The new values still apply for this session; losing them on disk is better than failing the action.
            _log.Error($"Could not save settings to {SettingsPath}: {ex.Message}");
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // Ignore: the temp file is overwritten by the next save.
            }
        }
    }
}

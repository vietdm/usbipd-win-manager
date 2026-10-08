using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Settings;

public sealed class SettingsStore : ISettingsStore
{
    /// <param name="settingsPath">Null means <c>%AppData%\UsbipdManager\settings.json</c>.</param>
    public SettingsStore(ILogService log, string? settingsPath = null)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? Changed;

    public string SettingsPath => throw new NotImplementedException();

    public AppSettings Current => throw new NotImplementedException();

    public void Update(Action<AppSettings> mutate) => throw new NotImplementedException();
}

using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

/// <summary>Thread-safe. <see cref="Changed"/> may be raised on any thread.</summary>
public interface ISettingsStore
{
    event EventHandler? Changed;

    string SettingsPath { get; }

    /// <summary>A snapshot; mutate through <see cref="Update"/> only.</summary>
    AppSettings Current { get; }

    /// <summary>Applies <paramref name="mutate"/>, persists to disk and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> mutate);
}

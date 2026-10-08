using System.Runtime.InteropServices;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Platform;

public sealed class ShortcutManager : IShortcutManager
{
    internal const string Description = "Switch USB devices between Windows and WSL2";

    private readonly ILogService _log;
    private readonly string _executablePath;
    private readonly ShortcutFolders _folders;

    public ShortcutManager(ILogService log, string executablePath)
        : this(log, executablePath, ShortcutFolders.FromEnvironment())
    {
    }

    internal ShortcutManager(ILogService log, string executablePath, ShortcutFolders folders)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(folders);

        _log = log;
        _executablePath = executablePath;
        _folders = folders;
    }

    public bool Exists(ShortcutLocation location) =>
        CandidatePaths(location).Any(File.Exists);

    public OperationResult Create(ShortcutLocation location)
    {
        var name = DisplayName(location);
        var folder = UserFolder(location);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return OperationResult.Fail($"Could not create the {name} shortcut: the folder is not available for this user.");
        }

        try
        {
            Directory.CreateDirectory(folder);
            ShellLink.Create(
                Path.Combine(folder, IShortcutManager.ShortcutFileName),
                _executablePath,
                Path.GetDirectoryName(_executablePath) ?? string.Empty,
                Description,
                _executablePath,
                0);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return OperationResult.Fail($"Could not create the {name} shortcut: {ex.Message}");
        }

        _log.Success($"{Capitalize(name)} shortcut created.");
        return OperationResult.Ok;
    }

    public OperationResult Remove(ShortcutLocation location)
    {
        var name = DisplayName(location);
        var errors = new List<string>();
        foreach (var path in CandidatePaths(location))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail($"Could not remove the {name} shortcut. {string.Join(" ", errors)}");
        }

        _log.Success($"{Capitalize(name)} shortcut removed.");
        return OperationResult.Ok;
    }

    private string UserFolder(ShortcutLocation location) => location switch
    {
        ShortcutLocation.Desktop => _folders.UserDesktop,
        ShortcutLocation.StartMenu => _folders.UserPrograms,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private string CommonFolder(ShortcutLocation location) => location switch
    {
        ShortcutLocation.Desktop => _folders.CommonDesktop,
        ShortcutLocation.StartMenu => _folders.CommonPrograms,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private IEnumerable<string> CandidatePaths(ShortcutLocation location) =>
        new[] { UserFolder(location), CommonFolder(location) }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => Path.Combine(f, IShortcutManager.ShortcutFileName));

    private static string DisplayName(ShortcutLocation location) => location switch
    {
        ShortcutLocation.Desktop => "desktop",
        ShortcutLocation.StartMenu => "Start Menu",
        _ => location.ToString(),
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>Where shortcuts live; empty strings mean "not available".</summary>
internal sealed record ShortcutFolders(string UserDesktop, string CommonDesktop, string UserPrograms, string CommonPrograms)
{
    public static ShortcutFolders FromEnvironment() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
}

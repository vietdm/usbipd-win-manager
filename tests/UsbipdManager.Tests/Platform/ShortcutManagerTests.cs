using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Platform;
using UsbipdManager.Tests.TestDoubles;

namespace UsbipdManager.Tests.Platform;

public sealed class ShortcutManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UsbipdManagerTests-" + Guid.NewGuid().ToString("N"));
    private readonly ShortcutFolders _folders;
    private readonly TestLog _log = new();
    private readonly string _exePath;

    public ShortcutManagerTests()
    {
        _folders = new ShortcutFolders(
            Path.Combine(_root, "UserDesktop"),
            Path.Combine(_root, "CommonDesktop"),
            Path.Combine(_root, "UserPrograms"),
            Path.Combine(_root, "CommonPrograms"));

        // The shell resolves the target, so point the shortcut at an existing file.
        var appDir = Directory.CreateDirectory(Path.Combine(_root, "App Folder")).FullName;
        _exePath = Path.Combine(appDir, "UsbipdManager.exe");
        File.WriteAllBytes(_exePath, []);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ShortcutManager CreateManager() => new(_log, _exePath, _folders);

    [Theory]
    [InlineData(ShortcutLocation.Desktop)]
    [InlineData(ShortcutLocation.StartMenu)]
    public void Create_then_exists_then_remove(ShortcutLocation location)
    {
        var manager = CreateManager();
        Assert.False(manager.Exists(location));

        var created = manager.Create(location);

        Assert.True(created.Success, created.Error);
        Assert.True(manager.Exists(location));
        var userFolder = location == ShortcutLocation.Desktop ? _folders.UserDesktop : _folders.UserPrograms;
        var linkPath = Path.Combine(userFolder, IShortcutManager.ShortcutFileName);
        Assert.True(File.Exists(linkPath));

        var info = ShellLink.Read(linkPath);
        Assert.Equal(_exePath, info.TargetPath, ignoreCase: true);
        Assert.Equal(Path.GetDirectoryName(_exePath), info.WorkingDirectory, ignoreCase: true);
        Assert.Equal("Switch USB devices between Windows and WSL2", info.Description);
        Assert.Equal(_exePath, info.IconPath, ignoreCase: true);
        Assert.Equal(0, info.IconIndex);

        var removed = manager.Remove(location);

        Assert.True(removed.Success, removed.Error);
        Assert.False(manager.Exists(location));
    }

    [Fact]
    public void Exists_detects_all_users_shortcut_and_remove_deletes_both()
    {
        Directory.CreateDirectory(_folders.CommonDesktop);
        var common = Path.Combine(_folders.CommonDesktop, IShortcutManager.ShortcutFileName);
        File.WriteAllBytes(common, [1]);
        var manager = CreateManager();

        Assert.True(manager.Exists(ShortcutLocation.Desktop));
        Assert.False(manager.Exists(ShortcutLocation.StartMenu));

        Assert.True(manager.Create(ShortcutLocation.Desktop).Success);
        var result = manager.Remove(ShortcutLocation.Desktop);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(common));
        Assert.False(manager.Exists(ShortcutLocation.Desktop));
    }

    [Fact]
    public void Remove_without_shortcuts_succeeds()
    {
        var result = CreateManager().Remove(ShortcutLocation.StartMenu);

        Assert.True(result.Success);
    }

    [Fact]
    public void Remove_reports_a_file_that_cannot_be_deleted()
    {
        Directory.CreateDirectory(_folders.UserPrograms);
        var path = Path.Combine(_folders.UserPrograms, IShortcutManager.ShortcutFileName);
        File.WriteAllBytes(path, [1]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = CreateManager().Remove(ShortcutLocation.StartMenu);

            Assert.False(result.Success);
            Assert.Contains("Start Menu", result.Error);
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Create_makes_a_missing_folder()
    {
        var result = CreateManager().Create(ShortcutLocation.StartMenu);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(Path.Combine(_folders.UserPrograms, IShortcutManager.ShortcutFileName)));
        Assert.True(_log.Contains(LogLevel.Success, "Start Menu shortcut created."));
    }
}

using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IShortcutManager
{
    const string ShortcutFileName = "USBIPD Manager.lnk";

    /// <summary>True if a shortcut exists for the current user or for all users (the installer creates all-users shortcuts).</summary>
    bool Exists(ShortcutLocation location);

    OperationResult Create(ShortcutLocation location);

    /// <summary>Removes both the current-user and the all-users shortcut.</summary>
    OperationResult Remove(ShortcutLocation location);
}

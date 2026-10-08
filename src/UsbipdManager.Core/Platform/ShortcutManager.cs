using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Platform;

public sealed class ShortcutManager : IShortcutManager
{
    public ShortcutManager(ILogService log, string executablePath)
    {
        throw new NotImplementedException();
    }

    public bool Exists(ShortcutLocation location) => throw new NotImplementedException();

    public OperationResult Create(ShortcutLocation location) => throw new NotImplementedException();

    public OperationResult Remove(ShortcutLocation location) => throw new NotImplementedException();
}

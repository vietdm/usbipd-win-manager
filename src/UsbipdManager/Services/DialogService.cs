using System.Windows;
using UsbipdManager.Views;

namespace UsbipdManager.Services;

public sealed class DialogService : IDialogService
{
    public void ShowMessage(string title, string message, DialogKind kind = DialogKind.Info) =>
        MessageDialog.Show(FindOwner(), title, message, kind, "OK");

    public bool Confirm(string title, string message, string confirmText, string cancelText = "Cancel", DialogKind kind = DialogKind.Question, bool destructive = false) =>
        MessageDialog.Show(FindOwner(), title, message, kind, confirmText, cancelText, destructive);

    /// <summary>The active visible window of the app (a modal Settings window wins over the main window).</summary>
    private static Window? FindOwner()
    {
        if (Application.Current is null)
        {
            return null;
        }

        var windows = Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible && w is not MessageDialog).ToList();
        return windows.FirstOrDefault(w => w.IsActive) ?? windows.LastOrDefault();
    }
}

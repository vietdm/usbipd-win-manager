namespace UsbipdManager.Services;

public enum DialogKind
{
    Info,
    Warning,
    Error,
    Question,
}

/// <summary>Styled dialogs (never the default WPF MessageBox). Call on the UI thread.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message, DialogKind kind = DialogKind.Info);

    /// <returns>True when the user chose <paramref name="confirmText"/>.</returns>
    bool Confirm(string title, string message, string confirmText, string cancelText = "Cancel", DialogKind kind = DialogKind.Question, bool destructive = false);
}

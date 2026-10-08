using UsbipdManager.Core.Abstractions;
using UsbipdManager.Mvvm;

namespace UsbipdManager.ViewModels;

/// <summary>One frequently asked question. <see cref="Command"/> is shown in a copyable box between the two answer parts.</summary>
public sealed class FaqItem
{
    public FaqItem(string question, string answer, string? command = null, string? followUp = null)
    {
        Question = question;
        Answer = answer;
        Command = command;
        FollowUp = followUp;
        CopyCommand = new RelayCommand(() => ConsoleViewModel.TrySetClipboard(Command!), () => Command is not null);
    }

    public string Question { get; }

    public string Answer { get; }

    public string? Command { get; }

    public string? FollowUp { get; }

    public bool HasCommand => Command is not null;

    public bool HasFollowUp => FollowUp is not null;

    public RelayCommand CopyCommand { get; }
}

public sealed class HelpViewModel
{
    public HelpViewModel(IAppInfo info)
    {
        IssueEmail = info.IssueEmail;
    }

    // Add new questions here; the window lists them in this order.
    public IReadOnlyList<FaqItem> Questions { get; } =
    [
        new FaqItem(
            "adb devices shows \"no permissions (missing udev rules?)\" in WSL",
            "The phone reached WSL, but Linux has no udev rule that lets your user open it. Install the Android udev rules inside WSL:",
            "sudo apt install android-sdk-platform-tools-common",
            "Then unplug the phone and plug it back in; USBIPD Manager attaches it to WSL2 again."),
    ];

    public string IssueEmail { get; }

    public string IssueText => $"Send issues to email {IssueEmail}";
}

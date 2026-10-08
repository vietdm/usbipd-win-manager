using UsbipdManager.Core.Abstractions;
using UsbipdManager.Mvvm;

namespace UsbipdManager.ViewModels;

/// <summary>One frequently asked question. <see cref="Command"/> is shown in a copyable box between the two answer parts.</summary>
public sealed class FaqItem : ObservableObject
{
    private bool _isExpanded;
    private bool _isVisible = true;

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

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>False when the question does not match the filter.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>Accordion of questions (at most one open) with a filter on the question text.</summary>
public sealed class HelpViewModel : ObservableObject
{
    private string _filterText = string.Empty;

    public HelpViewModel(IAppInfo info)
    {
        IssueEmail = info.IssueEmail;
        ToggleCommand = new RelayCommand(parameter =>
        {
            if (parameter is FaqItem item)
            {
                Toggle(item);
            }
        });
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

    public RelayCommand ToggleCommand { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public bool HasResults => Questions.Any(q => q.IsVisible);

    public string NoResultsText => $"No questions match \"{FilterText.Trim()}\".";

    public string IssueEmail { get; }

    public string IssueText => $"Send issues to email {IssueEmail}";

    public void Toggle(FaqItem item)
    {
        var expand = !item.IsExpanded;
        foreach (var question in Questions)
        {
            question.IsExpanded = false;
        }

        item.IsExpanded = expand;
    }

    // Every word of the filter must appear in the question, in any order.
    private void ApplyFilter()
    {
        var terms = FilterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var question in Questions)
        {
            question.IsVisible = terms.All(term => question.Question.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(NoResultsText));
    }
}

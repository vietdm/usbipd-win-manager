using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.ViewModels;

public sealed class AboutViewModel
{
    public AboutViewModel(IAppInfo info)
    {
        ProductName = info.ProductName;
        VersionText = $"Version {info.Version}";
        AuthorText = $"Author: {info.Author}";
        CreatedText = info.IsDevelopmentBuild || info.CreatedDate is null
            ? "Development build"
            : $"Created: {info.CreatedDate:yyyy-MM-dd}";
        UpdatedText = info.UpdatedDate is { } updated ? $"Updated: {updated:yyyy-MM-dd}" : null;
        Copyright = info.Copyright;
        IssueText = $"Send issues to email {info.IssueEmail}";
    }

    public string ProductName { get; }

    public string VersionText { get; }

    public string AuthorText { get; }

    public string CreatedText { get; }

    public string? UpdatedText { get; }

    public bool HasUpdated => UpdatedText is not null;

    public string Copyright { get; }

    public string IssueText { get; }
}

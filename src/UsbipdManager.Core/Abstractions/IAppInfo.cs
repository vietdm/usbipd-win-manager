namespace UsbipdManager.Core.Abstractions;

public interface IAppInfo
{
    string ProductName { get; }

    /// <summary>MAJOR.MINOR.PATCH.</summary>
    string Version { get; }

    string Author { get; }

    string Copyright { get; }

    string IssueEmail { get; }

    /// <summary>Set by the first <c>build.ps1 --release</c>; null for development builds.</summary>
    DateOnly? CreatedDate { get; }

    /// <summary>Set by later <c>build.ps1 --release</c> builds.</summary>
    DateOnly? UpdatedDate { get; }

    bool IsDevelopmentBuild { get; }

    string ExecutablePath { get; }
}

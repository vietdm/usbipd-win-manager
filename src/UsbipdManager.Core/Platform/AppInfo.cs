using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Platform;

/// <summary>Product data; version and dates come from the entry assembly attributes written by build.ps1.</summary>
public sealed class AppInfo : IAppInfo
{
    public static AppInfo FromEntryAssembly() => throw new NotImplementedException();

    public string ProductName => throw new NotImplementedException();

    public string Version => throw new NotImplementedException();

    public string Author => throw new NotImplementedException();

    public string Copyright => throw new NotImplementedException();

    public string IssueEmail => throw new NotImplementedException();

    public DateOnly? CreatedDate => throw new NotImplementedException();

    public DateOnly? UpdatedDate => throw new NotImplementedException();

    public bool IsDevelopmentBuild => throw new NotImplementedException();

    public string ExecutablePath => throw new NotImplementedException();
}

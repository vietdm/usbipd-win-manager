using System.Globalization;
using System.Reflection;
using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Platform;

/// <summary>Product data; version and dates come from the entry assembly attributes written by build.ps1.</summary>
public sealed class AppInfo : IAppInfo
{
    internal const string DefaultCopyright = "© 2026 Minh Viet";
    internal const string DateFormat = "yyyy-MM-dd";

    internal AppInfo(string version, string copyright, DateOnly? createdDate, DateOnly? updatedDate, string executablePath)
    {
        Version = version;
        Copyright = string.IsNullOrWhiteSpace(copyright) ? DefaultCopyright : copyright;
        CreatedDate = createdDate;
        UpdatedDate = updatedDate;
        ExecutablePath = executablePath;
    }

    public static AppInfo FromEntryAssembly() =>
        FromAssembly(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    internal static AppInfo FromAssembly(Assembly assembly, string? executablePath = null)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        return new AppInfo(
            NormalizeVersion(informational, assembly.GetName().Version),
            copyright ?? DefaultCopyright,
            ParseDate(metadata.GetValueOrDefault("CreatedDate")),
            ParseDate(metadata.GetValueOrDefault("UpdatedDate")),
            executablePath ?? Environment.ProcessPath ?? string.Empty);
    }

    public string ProductName => "USBIPD Manager";

    public string Version { get; }

    public string Author => "Minh Viet";

    public string Copyright { get; }

    public string IssueEmail => "vietdau33@gmail.com";

    public DateOnly? CreatedDate { get; }

    public DateOnly? UpdatedDate { get; }

    public bool IsDevelopmentBuild => CreatedDate is null;

    public string ExecutablePath { get; }

    /// <summary>
    /// Returns MAJOR.MINOR.PATCH from the informational version (build metadata and pre-release tags dropped),
    /// falling back to the first three parts of the assembly version.
    /// </summary>
    internal static string NormalizeVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var core = informationalVersion.Trim();
            var cut = core.IndexOfAny(['+', '-', ' ']);
            if (cut >= 0)
            {
                core = core[..cut];
            }

            var parts = core.Split('.');
            if (parts.Length is >= 1 and <= 4 && parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                var numbers = parts.Select(p => int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture))
                    .Concat([0, 0, 0])
                    .Take(3);
                return string.Join('.', numbers);
            }
        }

        if (assemblyVersion is null)
        {
            return "0.0.0";
        }

        return string.Join('.', assemblyVersion.Major, Math.Max(assemblyVersion.Minor, 0), Math.Max(assemblyVersion.Build, 0));
    }

    internal static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}

using System.Security;

namespace UsbipdManager.Core.Usbipd;

internal static class ExecutableLocator
{
    /// <summary>
    /// Searches the PATH read fresh from the registry (Machine + User), then the process PATH.
    /// The process PATH alone is stale when a tool was installed (e.g. by winget) after the app started.
    /// </summary>
    public static string? FindOnPath(string fileName)
    {
        foreach (var directory in GetPathDirectories())
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPathDirectories()
    {
        var values = new[]
        {
            ReadPath(EnvironmentVariableTarget.Machine),
            ReadPath(EnvironmentVariableTarget.User),
            ReadPath(EnvironmentVariableTarget.Process),
        };

        return values
            .SelectMany(value => (value ?? string.Empty).Split(';'))
            .Select(entry => Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')))
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadPath(EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable("Path", target);
        }
        catch (SecurityException)
        {
            return null;
        }
    }
}

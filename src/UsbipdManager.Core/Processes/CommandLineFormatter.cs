namespace UsbipdManager.Core.Processes;

internal static class CommandLineFormatter
{
    /// <summary><c>&gt; usbipd attach --wsl --busid 1-4</c>: the executable name without directory or <c>.exe</c>, arguments with spaces quoted.</summary>
    public static string Format(string fileName, IReadOnlyList<string> arguments)
    {
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var parts = new List<string>(arguments.Count + 1) { name };
        parts.AddRange(arguments.Select(Quote));
        return "> " + string.Join(' ', parts);
    }

    private static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
    }
}

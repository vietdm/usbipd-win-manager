using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

internal static class UsbipdErrors
{
    private const string ErrorPrefix = "usbipd: error:";
    private const string ToolPrefix = "usbipd:";

    /// <summary>
    /// The text of the first <c>usbipd: error: ...</c> line (stderr first, then stdout), else the last non-empty output line,
    /// else a generic message with the exit code.
    /// </summary>
    public static string Extract(ProcessResult result)
    {
        var stderrLines = Lines(result.StandardError);
        var stdoutLines = Lines(result.StandardOutput);

        var errorLine = stderrLines.Concat(stdoutLines)
            .FirstOrDefault(line => line.Contains(ErrorPrefix, StringComparison.OrdinalIgnoreCase));
        if (errorLine is not null)
        {
            var text = errorLine[(errorLine.IndexOf(ErrorPrefix, StringComparison.OrdinalIgnoreCase) + ErrorPrefix.Length)..].Trim();
            if (text.Length > 0)
            {
                return text;
            }
        }

        var lastLine = stderrLines.LastOrDefault() ?? stdoutLines.LastOrDefault();
        if (lastLine is not null)
        {
            return StripToolPrefix(lastLine);
        }

        return $"usbipd exited with code {result.ExitCode}.";
    }

    private static string StripToolPrefix(string line)
    {
        if (!line.StartsWith(ToolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return line;
        }

        // "usbipd: warning: text" -> "text"
        var rest = line[ToolPrefix.Length..].TrimStart();
        var colon = rest.IndexOf(':');
        return colon > 0 && colon < 12 && !rest[..colon].Contains(' ') ? rest[(colon + 1)..].Trim() : rest;
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
}

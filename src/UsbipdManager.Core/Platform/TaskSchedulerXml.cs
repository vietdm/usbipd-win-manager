using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace UsbipdManager.Core.Platform;

/// <summary>Builds and reads the Task Scheduler XML used by <see cref="AutoStartManager"/>.</summary>
internal static partial class TaskSchedulerXml
{
    internal static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    internal const string TrayArgument = "--tray";

    public static XDocument Build(string executablePath, string userId, string taskName)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;

        // Element order follows what Task Scheduler itself exports.
        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Author", "Minh Viet"),
                    new XElement(Ns + "Description", "Starts USBIPD Manager in the tray at logon."),
                    new XElement(Ns + "URI", "\\" + taskName)),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "Delay", "PT5S"))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "HighestAvailable"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "false"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    // The Task Scheduler default (7) runs the app at below-normal priority.
                    new XElement(Ns + "Priority", "6")),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", executablePath),
                        new XElement(Ns + "Arguments", TrayArgument),
                        new XElement(Ns + "WorkingDirectory", workingDirectory)))));
    }

    /// <summary>Writes the document as UTF-16 with a BOM, which is what <c>schtasks /Create /XML</c> expects.</summary>
    public static void Save(XDocument document, string path)
    {
        var settings = new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true };
        using var writer = XmlWriter.Create(path, settings);
        document.Save(writer);
    }

    /// <summary>Parses the output of <c>schtasks /Query /XML</c>; null when it is not usable XML.</summary>
    public static QueriedTask? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var start = xml.IndexOf('<');
        if (start < 0)
        {
            return null;
        }

        try
        {
            var root = XDocument.Parse(xml[start..]).Root;
            if (root is null)
            {
                return null;
            }

            var command = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Exec")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            var enabled = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Settings")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled")?.Value;
            return new QueriedTask(command?.Trim(), !IsFalse(enabled));
        }
        catch (XmlException)
        {
            // Console encoding can make the declared encoding or some characters unreadable; fall back to a text scan.
            var command = CommandRegex().Match(xml);
            var settings = SettingsRegex().Match(xml);
            var enabled = settings.Success ? EnabledRegex().Match(settings.Value) : Match.Empty;
            return new QueriedTask(
                command.Success ? System.Net.WebUtility.HtmlDecode(command.Groups[1].Value).Trim() : null,
                !(enabled.Success && IsFalse(enabled.Groups[1].Value)));
        }
    }

    /// <summary>Case-insensitive comparison of two exe paths after removing quotes, expanding variables and normalizing.</summary>
    public static bool SamePath(string? left, string? right)
    {
        var a = NormalizePath(left);
        var b = NormalizePath(right);
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"').Trim());
        try
        {
            value = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the raw value; the comparison will simply report a difference.
        }

        return value.TrimEnd('\\', '/');
    }

    private static bool IsFalse(string? value) => string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"<Exec>.*?<Command>(.*?)</Command>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(@"<Settings>.*?</Settings>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SettingsRegex();

    // Only the direct child: IdleSettings etc. have no Enabled element, so the first match in Settings is the task flag.
    [GeneratedRegex(@"<Enabled>\s*(\w+)\s*</Enabled>", RegexOptions.IgnoreCase)]
    private static partial Regex EnabledRegex();
}

internal sealed record QueriedTask(string? Command, bool Enabled);

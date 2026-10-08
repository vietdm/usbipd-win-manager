namespace UsbipdManager.Startup;

public enum StartupCommand
{
    /// <summary>Normal start (window shown unless <see cref="CommandLineOptions.StartInTray"/>).</summary>
    Run,
    RegisterAutoStart,
    UnregisterAutoStart,
    Exit,
}

/// <summary>The command-line contract of docs/PLAN.md section 6.1.</summary>
public sealed record CommandLineOptions(StartupCommand Command, bool StartInTray, IReadOnlyList<string> UnknownArguments)
{
    public bool IsHeadless => Command != StartupCommand.Run;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var command = StartupCommand.Run;
        var tray = false;
        var unknown = new List<string>();

        foreach (var raw in args)
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "--tray":
                    tray = true;
                    break;
                case "--register-autostart":
                    command = StartupCommand.RegisterAutoStart;
                    break;
                case "--unregister-autostart":
                    command = StartupCommand.UnregisterAutoStart;
                    break;
                case "--exit":
                    command = StartupCommand.Exit;
                    break;
                case "":
                    break;
                default:
                    unknown.Add(raw);
                    break;
            }
        }

        return new CommandLineOptions(command, tray, unknown);
    }
}

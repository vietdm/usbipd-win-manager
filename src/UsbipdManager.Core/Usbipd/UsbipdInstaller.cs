using System.ComponentModel;
using System.Text;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

public sealed class UsbipdInstaller : IUsbipdInstaller
{
    public const string WingetPackageId = "dorssel.usbipd-win";

    /// <summary>APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED / no applicable upgrade.</summary>
    internal const int AlreadyInstalledExitCode = unchecked((int)0x8A15002B);

    /// <summary>APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH: installed, a restart completes it.</summary>
    internal const int RebootRequiredExitCode = unchecked((int)0x8A150109);

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private readonly IProcessRunner _runner;
    private readonly ILogService _log;
    private readonly Func<string?> _resolveWinget;

    public UsbipdInstaller(IProcessRunner runner, ILogService log)
        : this(runner, log, null)
    {
    }

    /// <param name="resolveWinget">Test seam; null means the real lookup.</param>
    internal UsbipdInstaller(IProcessRunner runner, ILogService log, Func<string?>? resolveWinget)
    {
        _runner = runner;
        _log = log;
        _resolveWinget = resolveWinget ?? FindWinget;
    }

    public bool IsWingetAvailable() => _resolveWinget() is not null;

    public async Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        var winget = _resolveWinget();
        if (winget is null)
        {
            return OperationResult.Fail($"winget is not available. Install usbipd-win from {IUsbipdInstaller.ReleasePageUrl}");
        }

        _log.Info("Installing usbipd-win with winget. This can take a few minutes...");

        string[] arguments =
        [
            "install", "--id", WingetPackageId, "-e", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        ];

        ProcessResult result;
        try
        {
            var options = new ProcessRunOptions { Timeout = InstallTimeout, OutputEncoding = Encoding.UTF8 };
            result = await _runner.RunAsync(winget, arguments, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail($"Could not start winget: {ex.Message}");
        }

        if (result.TimedOut)
        {
            return OperationResult.Fail($"winget did not finish within {InstallTimeout.TotalMinutes:0} minutes.");
        }

        switch (result.ExitCode)
        {
            case 0:
                _log.Success("usbipd-win is installed.");
                return OperationResult.Ok;
            case AlreadyInstalledExitCode:
                _log.Success("usbipd-win is already installed.");
                return OperationResult.Ok;
            case RebootRequiredExitCode:
                _log.Success("usbipd-win is installed.");
                _log.Warning("Restart Windows to finish the usbipd-win installation.");
                return OperationResult.Ok;
        }

        var reason = LastMeaningfulLine(result.StandardOutput) ?? LastMeaningfulLine(result.StandardError);
        return OperationResult.Fail(reason is null
            ? $"winget failed with exit code 0x{result.ExitCode:X8}."
            : $"winget failed: {reason}");
    }

    internal static string? FindWinget()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
        {
            // An app execution alias: File.Exists sees it and CreateProcess can start it.
            var aliasPath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(aliasPath))
            {
                return aliasPath;
            }
        }

        return ExecutableLocator.FindOnPath("winget.exe");
    }

    /// <summary>winget redraws spinners and progress bars with '\r'; only lines with real words are meaningful.</summary>
    internal static string? LastMeaningfulLine(string output) =>
        output
            .Split(['\r', '\n'])
            .Select(line => line.Trim())
            .LastOrDefault(IsMeaningful);

    private static bool IsMeaningful(string line)
    {
        if (line.Length < 3 || line.Any(c => c is '█' or '▒' or '░'))
        {
            return false;
        }

        return line.Count(char.IsLetter) >= 3;
    }
}

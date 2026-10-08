using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Wsl;

/// <summary>Parses the table printed by <c>wsl.exe -l -v</c>.</summary>
internal static class WslListParser
{
    /// <summary>
    /// Older WSL versions ignore <c>WSL_UTF8=1</c> and print UTF-16LE. Decoded as UTF-8 that text has a NUL after every
    /// ASCII character and its BOM turns into replacement characters, so both are removed.
    /// </summary>
    public static string Normalize(string output) =>
        output.Replace("\0", string.Empty).Replace("﻿", string.Empty).Replace("�", string.Empty);

    /// <summary>
    /// Rows look like <c>* Ubuntu-26.04    Running         2</c>. The header and any message (e.g. "no installed distributions")
    /// are skipped because their last token is not a version number.
    /// </summary>
    public static IReadOnlyList<WslDistro> Parse(string output)
    {
        var distros = new List<WslDistro>();
        foreach (var rawLine in Normalize(output).Split('\n'))
        {
            var line = rawLine.Trim();
            var isDefault = line.StartsWith('*');
            if (isDefault)
            {
                line = line[1..].TrimStart();
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3 || !int.TryParse(tokens[^1], out var version) || version is < 1 or > 9)
            {
                continue;
            }

            // The state can contain spaces in localized output, so it is everything between the name and the version.
            var state = string.Join(' ', tokens[1..^1]);
            distros.Add(new WslDistro(
                Name: tokens[0],
                IsRunning: state.Equals("Running", StringComparison.OrdinalIgnoreCase),
                Version: version,
                IsDefault: isDefault));
        }

        return distros;
    }
}

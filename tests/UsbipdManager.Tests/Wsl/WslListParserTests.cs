using System.Text;
using UsbipdManager.Core.Models;
using UsbipdManager.Core.Wsl;

namespace UsbipdManager.Tests.Wsl;

public sealed class WslListParserTests
{
    private const string Table =
        "  NAME            STATE           VERSION\r\n" +
        "* Ubuntu-26.04    Running         2\r\n" +
        "  Debian          Stopped         1\r\n";

    private static readonly WslDistro[] Expected =
    [
        new("Ubuntu-26.04", IsRunning: true, Version: 2, IsDefault: true),
        new("Debian", IsRunning: false, Version: 1, IsDefault: false),
    ];

    [Fact]
    public void Parse_Utf8Table()
    {
        Assert.Equal(Expected, WslListParser.Parse(Table));
    }

    [Fact]
    public void Parse_Utf16OutputDecodedAsUtf8_WithNulsAndBom()
    {
        // What the runner returns when an older wsl.exe ignores WSL_UTF8 and prints UTF-16LE with a BOM.
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Table)).ToArray();
        var decoded = Encoding.UTF8.GetString(bytes);

        Assert.Contains('\0', decoded);
        Assert.Equal(Expected, WslListParser.Parse(decoded));
    }

    [Fact]
    public void Parse_Utf16OutputWithBomCharacter()
    {
        Assert.Equal(Expected, WslListParser.Parse("﻿" + Table));
    }

    [Fact]
    public void Parse_NoDefaultMarker()
    {
        var distros = WslListParser.Parse("  NAME      STATE      VERSION\n  Alpine    Stopped    2\n");

        var distro = Assert.Single(distros);
        Assert.Equal(new WslDistro("Alpine", false, 2, false), distro);
    }

    [Fact]
    public void Parse_NoDistributionMessage_ReturnsEmpty()
    {
        const string message =
            "Windows Subsystem for Linux has no installed distributions.\r\n" +
            "You can resolve this by installing a distribution with the instructions below:\r\n\r\n" +
            "Use 'wsl.exe --list --online' to list available distributions\r\n" +
            "and 'wsl.exe --install <Distro>' to install.\r\n";

        Assert.Empty(WslListParser.Parse(message));
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(WslListParser.Parse(string.Empty));
    }
}

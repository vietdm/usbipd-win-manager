using UsbipdManager.Core.Platform;

namespace UsbipdManager.Tests.Platform;

public sealed class AppInfoTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef0123", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("2", "2.0.0")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("1.2.3-beta.1+sha", "1.2.3")]
    [InlineData(" 1.0.4 ", "1.0.4")]
    public void Version_is_normalized_from_the_informational_version(string informational, string expected) =>
        Assert.Equal(expected, AppInfo.NormalizeVersion(informational, new Version(9, 9, 9, 9)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.x.3")]
    public void Version_falls_back_to_the_assembly_version(string? informational) =>
        Assert.Equal("4.5.6", AppInfo.NormalizeVersion(informational, new Version(4, 5, 6, 7)));

    [Fact]
    public void Version_fallback_pads_a_two_part_assembly_version() =>
        Assert.Equal("4.5.0", AppInfo.NormalizeVersion(null, new Version(4, 5)));

    [Theory]
    [InlineData("2026-03-15", 2026, 3, 15)]
    [InlineData(" 2026-12-01 ", 2026, 12, 1)]
    public void Dates_are_parsed_invariantly(string value, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), AppInfo.ParseDate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("15/03/2026")]
    [InlineData("2026-13-01")]
    public void Empty_or_invalid_dates_are_null(string? value) =>
        Assert.Null(AppInfo.ParseDate(value));

    [Fact]
    public void Development_build_has_no_created_date()
    {
        var dev = new AppInfo("1.0.0", "", null, null, @"C:\app.exe");
        var release = new AppInfo("1.0.0", "© Someone", new DateOnly(2026, 1, 2), null, @"C:\app.exe");

        Assert.True(dev.IsDevelopmentBuild);
        Assert.Equal("© 2026 Minh Viet", dev.Copyright);
        Assert.False(release.IsDevelopmentBuild);
        Assert.Equal("© Someone", release.Copyright);
    }

    [Fact]
    public void Constant_product_data()
    {
        var info = AppInfo.FromAssembly(typeof(AppInfo).Assembly, @"C:\app.exe");

        Assert.Equal("USBIPD Manager", info.ProductName);
        Assert.Equal("Minh Viet", info.Author);
        Assert.Equal("vietdau33@gmail.com", info.IssueEmail);
        Assert.Equal(@"C:\app.exe", info.ExecutablePath);
        Assert.Matches(@"^\d+\.\d+\.\d+$", info.Version);
        Assert.False(string.IsNullOrWhiteSpace(info.Copyright));
    }

    [Fact]
    public void From_entry_assembly_does_not_throw()
    {
        var info = AppInfo.FromEntryAssembly();

        Assert.Matches(@"^\d+\.\d+\.\d+$", info.Version);
        Assert.Equal(Environment.ProcessPath, info.ExecutablePath);
    }
}

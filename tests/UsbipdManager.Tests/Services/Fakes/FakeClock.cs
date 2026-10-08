namespace UsbipdManager.Tests.Services.Fakes;

public sealed class FakeClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(7));

    public void Advance(TimeSpan by) => Now += by;
}

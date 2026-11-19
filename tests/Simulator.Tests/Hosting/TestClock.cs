namespace MyWealthV2.Simulator.Tests.Hosting;

/// <summary>A real clock the test moves by hand.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public static DateTimeOffset Start { get; } = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    public TestClock() : this(Start)
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}

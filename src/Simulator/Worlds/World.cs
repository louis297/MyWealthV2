namespace MyWealthV2.Simulator.Worlds;

/// <summary>
/// The one simulator world (simulator spec R20–R24, R27). Simulator now is real UTC now plus
/// <see cref="ClockOffsetSeconds"/> while running, and <see cref="PausedAt"/> while paused. The offset is never
/// negative and no command moves simulator now back.
/// </summary>
public sealed class World
{
    public const int SingletonId = 1;

    /// <summary>R22: the furthest one advance may move the clock.</summary>
    public static readonly TimeSpan MaxAdvance = TimeSpan.FromDays(366);

    private World()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public Guid WorldId { get; private set; }

    public int Seed { get; private set; }

    public long ClockOffsetSeconds { get; private set; }

    public DateTimeOffset? PausedAt { get; private set; }

    public bool PartyOutage { get; private set; }

    /// <summary>Real instant of the last reset (or of the first start).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool Paused => PausedAt is not null;

    public static World Create(Guid worldId, int seed, DateTimeOffset realNow)
    {
        var world = new World();
        world.Reset(worldId, seed, realNow);
        return world;
    }

    public DateTimeOffset Now(DateTimeOffset realNow) =>
        PausedAt ?? realNow.ToUniversalTime().AddSeconds(ClockOffsetSeconds);

    /// <summary>R22: <c>by</c> is greater than zero and at most <see cref="MaxAdvance"/>.</summary>
    public void Advance(TimeSpan by, DateTimeOffset realNow)
    {
        if (by <= TimeSpan.Zero)
        {
            throw new WorldRuleException("By", "The duration must be greater than zero.");
        }

        if (by > MaxAdvance)
        {
            throw new WorldRuleException("By", "The duration must be at most P366D.");
        }

        MoveTo(Now(realNow) + by, realNow);
    }

    /// <summary>R22: <c>to</c> is later than simulator now and at most <see cref="MaxAdvance"/> ahead.</summary>
    public void AdvanceTo(DateTimeOffset to, DateTimeOffset realNow)
    {
        var now = Now(realNow);
        if (to <= now)
        {
            throw new WorldRuleException("To", "The instant must be later than the simulator's now.");
        }

        if (to - now > MaxAdvance)
        {
            throw new WorldRuleException("To", "The instant must be at most 366 days ahead of the simulator's now.");
        }

        MoveTo(to, realNow);
    }

    /// <summary>R23: freezes simulator now. Pausing a paused clock changes nothing.</summary>
    public void Pause(DateTimeOffset realNow)
    {
        if (Paused)
        {
            return;
        }

        PausedAt = Now(realNow);
    }

    /// <summary>
    /// R23: continues from the paused value without a jump. When the paused value is behind real now, the
    /// offset would have to be negative (R21), so the clock continues at real now instead. Resuming a running
    /// clock changes nothing.
    /// </summary>
    public void Resume(DateTimeOffset realNow)
    {
        if (PausedAt is not { } pausedAt)
        {
            return;
        }

        ClockOffsetSeconds = OffsetFor(pausedAt, realNow);
        PausedAt = null;
    }

    public void SetOutage(bool on) => PartyOutage = on;

    /// <summary>R24: a new world, offset zero, running, no outage.</summary>
    public void Reset(Guid worldId, int seed, DateTimeOffset realNow)
    {
        WorldId = worldId;
        Seed = seed;
        ClockOffsetSeconds = 0;
        PausedAt = null;
        PartyOutage = false;
        StartedAt = realNow.ToUniversalTime();
    }

    private void MoveTo(DateTimeOffset target, DateTimeOffset realNow)
    {
        if (Paused)
        {
            PausedAt = target;
            return;
        }

        ClockOffsetSeconds = OffsetFor(target, realNow);
    }

    // Whole seconds, rounded up, so simulator now never lands before the target.
    private static long OffsetFor(DateTimeOffset target, DateTimeOffset realNow)
    {
        var seconds = (target - realNow).Ticks / (double)TimeSpan.TicksPerSecond;
        return Math.Max(0, (long)Math.Ceiling(seconds));
    }
}

/// <summary>A control write the world refuses; <see cref="Field"/> names the body field (400 <c>errors</c>).</summary>
public sealed class WorldRuleException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

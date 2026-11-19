namespace MyWealthV2.Simulator.Worlds;

/// <summary>Simulator spec R20–R24, R27. Stub.</summary>
public sealed class World
{
    public int Id { get; private set; }

    public Guid WorldId { get; private set; }

    public int Seed { get; private set; }

    public long ClockOffsetSeconds { get; private set; }

    public DateTimeOffset? PausedAt { get; private set; }

    public bool PartyOutage { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool Paused => PausedAt is not null;

    public static World Create(Guid worldId, int seed, DateTimeOffset realNow) => new();

    public DateTimeOffset Now(DateTimeOffset realNow) => default;

    public void Advance(TimeSpan by, DateTimeOffset realNow)
    {
    }

    public void AdvanceTo(DateTimeOffset to, DateTimeOffset realNow)
    {
    }

    public void Pause(DateTimeOffset realNow)
    {
    }

    public void Resume(DateTimeOffset realNow)
    {
    }

    public void SetOutage(bool on)
    {
    }

    public void Reset(Guid worldId, int seed, DateTimeOffset realNow)
    {
    }
}

/// <summary>A control write the world refuses; <see cref="Field"/> names the body field (400 <c>errors</c>).</summary>
public sealed class WorldRuleException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

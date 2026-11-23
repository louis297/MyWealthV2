using System.Text.Json;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R3–R5, §8.1: the body of create and replace.</summary>
public abstract class MigrationBatchBody
{
    public string? Name { get; init; }

    public string? PreviousProvider { get; init; }

    public DateOnly? AsOfDate { get; init; }

    public MigrationControlTotalsBody? ControlTotals { get; init; }

    public List<MigrationEntryBody>? Entries { get; init; }

    public JsonElement? TenantId { get; init; }
}

public sealed class MigrationControlTotalsBody
{
    public int? AccountCount { get; init; }

    public List<MigrationMoneyBody>? Cash { get; init; }

    public List<MigrationQuantityBody>? Quantity { get; init; }

    public List<MigrationMoneyBody>? Cost { get; init; }
}

public sealed class MigrationMoneyBody
{
    public string? Currency { get; init; }

    public decimal? Amount { get; init; }
}

public sealed class MigrationQuantityBody
{
    public Guid? InstrumentId { get; init; }

    public decimal? Quantity { get; init; }
}

public sealed class MigrationEntryBody
{
    public Guid? AccountId { get; init; }

    public string? ExternalReference { get; init; }

    public decimal? Cash { get; init; }

    public List<MigrationHoldingBody>? Holdings { get; init; }
}

public sealed class MigrationHoldingBody
{
    public Guid? InstrumentId { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Cost { get; init; }
}

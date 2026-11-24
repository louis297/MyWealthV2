using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>A corporate action item (docs/features/corporate-actions.md R35, §8.1).</summary>
public sealed class CorporateActionDto
{
    public required Guid Id { get; init; }

    public required CorporateActionInstrumentDto Instrument { get; init; }

    public required string Kind { get; init; }

    public required int? RatioNew { get; init; }

    public required int? RatioHeld { get; init; }

    public required DateOnly ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public required string Origin { get; init; }

    public required string Status { get; init; }

    public string? Memo { get; init; }

    /// <summary>The person who entered it; <c>null</c> for the platform.</summary>
    public PersonRefDto? CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? EffectiveAt { get; init; }

    public PersonRefDto? EffectiveBy { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }

    public PersonRefDto? CancelledBy { get; init; }

    public string? CancelNote { get; init; }

    /// <summary>Dividends R49: the terms of a dividend; <c>null</c> on another kind.</summary>
    public CorporateActionDividendDto? Dividend { get; init; }

    /// <summary>Dividends R49: what the Customers are owed and what the custodian paid; <c>null</c> until the dividend
    /// goes ex, and on another kind.</summary>
    public CorporateActionPaymentDto? Payment { get; init; }

    public required CorporateActionEntitlementCountsDto Entitlements { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record CorporateActionInstrumentDto(Guid Id, string Symbol, string Name);

/// <summary>Dividends R49: the terms of a dividend, in the Instrument's quote currency.</summary>
public sealed record CorporateActionDividendDto(
    decimal AmountPerShare,
    string Currency,
    string TaxSource,
    decimal ImputationCreditPerShare,
    decimal ForeignTaxRatePercent,
    decimal? ReinvestmentPrice);

/// <summary>Dividends R15, R49: the payment of a dividend. The received amount, the difference and the statement line
/// are <c>null</c> until the receipt; the statement line is <c>null</c> for an Adviser.</summary>
public sealed record CorporateActionPaymentDto(
    decimal ExpectedAmount,
    decimal? ReceivedAmount,
    decimal? Difference,
    string Currency,
    Guid? StatementLineId,
    DateTimeOffset? PaidAt);

/// <summary>R35: the entitlements the caller can see, by status; all 0 before the action is effective.</summary>
public sealed record CorporateActionEntitlementCountsDto(int Total, int Posted, int Pending, int NotApplied);

public sealed record CreatedCorporateActionDto(Guid Id);

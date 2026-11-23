using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.TradeSettlements;

/// <summary>A trade settlement as the API returns it (docs/features/trade-settlement.md §8.1). The list item has no
/// <see cref="Fills"/> and no <see cref="Payments"/>.</summary>
public sealed record TradeSettlementDto
{
    public required Guid Id { get; init; }

    public required TradeSettlementClientMoneyDto ClientMoneyAccount { get; init; }

    public required string Connection { get; init; }

    public required string BrokerSettlementId { get; init; }

    public required DateOnly SettledDate { get; init; }

    public required int FillCount { get; init; }

    public required decimal NetAmount { get; init; }

    public required TradeSettlementBrokerDto Broker { get; init; }

    public required string Status { get; init; }

    public string? Failure { get; init; }

    public string? FailureDetail { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public Guid? StatementLineId { get; init; }

    public TradeSettlementSetAsideDto? SetAside { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TradeSettlementPaymentDto>? Payments { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TradeSettlementFillDto>? Fills { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record TradeSettlementClientMoneyDto(Guid Id, string Currency, string AccountNumber);

public sealed record TradeSettlementBrokerDto(int FillCount, decimal NetAmount, string SettlementAccount);

public sealed record TradeSettlementSetAsideDto(PersonRefDto? By, DateTimeOffset At, string Note);

public sealed record TradeSettlementPaymentDto(string Status, int Attempts, string? LastFailure, DateTimeOffset CreatedAt);

public sealed record TradeSettlementFillDto(
    Guid Id,
    Guid OrderId,
    Guid AccountId,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Gross,
    string Status);

namespace MyWealthV2.Domain.Orders;

/// <summary>
/// The settlement fields of an execution report (docs/features/trade-settlement.md R6, R7): the broker settlement a
/// <c>Settled</c> report names, and the figures a <c>Settlement</c> report closes it with. <see cref="NetAmount"/> is
/// seen from the trust account: above 0 the broker pays the firm.
/// </summary>
public sealed record ExecutionReportSettlement(
    string? SettlementId,
    string? SettlementAccount = null,
    string? Currency = null,
    decimal? NetAmount = null,
    int? FillCount = null);

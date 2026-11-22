using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The broker's execution behind an order fill's posting (orders R35): the fill's PublicId, the price, the trade
/// date, the settlement date and the broker's reference. Exactly one on every Transaction with source
/// <c>OrderFill</c> and on its Reversal, none elsewhere; immutable. Whether the fill has settled is on the fill.
/// </summary>
public class TransactionExecutionDetail
{
    public const int BrokerReferenceMaxLength = 50;

    private TransactionExecutionDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public Guid FillPublicId { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly TradeDate { get; private set; }

    public DateOnly SettlementDate { get; private set; }

    /// <summary>The broker's contract note; null when the broker sent none.</summary>
    public string? BrokerReference { get; private set; }

    internal static TransactionExecutionDetail Create(int tenantId, TradeExecution execution, decimal price) =>
        throw new NotImplementedException();

    /// <summary>A new row with the same values, for the Reversal (R35).</summary>
    internal TransactionExecutionDetail Copy() => throw new NotImplementedException();
}

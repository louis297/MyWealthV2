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

    internal static TransactionExecutionDetail Create(int tenantId, TradeExecution execution, decimal price)
    {
        if (execution.FillPublicId == Guid.Empty)
        {
            throw new DomainException("An execution detail needs the fill's PublicId.");
        }

        if (execution.SettlementDate < execution.TradeDate)
        {
            throw new DomainException("A fill cannot settle before its trade date.");
        }

        if (price <= 0m)
        {
            throw new DomainException("Price must be greater than 0.");
        }

        var reference = execution.BrokerReference?.Trim();
        return new TransactionExecutionDetail
        {
            TenantId = tenantId,
            FillPublicId = execution.FillPublicId,
            Price = price,
            TradeDate = execution.TradeDate,
            SettlementDate = execution.SettlementDate,
            BrokerReference = string.IsNullOrEmpty(reference)
                ? null
                : reference.Length > BrokerReferenceMaxLength ? reference[..BrokerReferenceMaxLength] : reference
        };
    }

    /// <summary>A new row with the same values, for the Reversal (R35).</summary>
    internal TransactionExecutionDetail Copy() =>
        new()
        {
            TenantId = TenantId,
            FillPublicId = FillPublicId,
            Price = Price,
            TradeDate = TradeDate,
            SettlementDate = SettlementDate,
            BrokerReference = BrokerReference
        };
}

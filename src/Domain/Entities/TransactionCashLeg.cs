namespace MyWealthV2.Domain.Entities;

public class TransactionCashLeg : BaseAuditableEntity
{
    private TransactionCashLeg()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public int AccountId { get; private set; }

    public CashLegRole Role { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The fee behind a <see cref="CashLegRole.Fee"/> leg; null on a <see cref="CashLegRole.Principal"/> leg.</summary>
    public TransactionFeeDetail? FeeDetail { get; private set; }

    /// <summary>The withholding tax behind a <see cref="CashLegRole.Tax"/> leg; null on every other leg (interest-run R25).</summary>
    public TransactionTaxDetail? TaxDetail { get; private set; }

    internal static TransactionCashLeg Create(int tenantId, int accountId, decimal amount, string currency) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Amount = amount,
            Currency = currency
        };

    /// <summary>The opposite leg for a Reversal: same Account, role and fee detail values (trade-fees R19).</summary>
    internal TransactionCashLeg ReversalLeg()
    {
        if (Role == CashLegRole.Fee && FeeDetail is null)
        {
            throw new DomainException("A fee leg needs its fee detail to be reversed.");
        }

        if (Role == CashLegRole.Tax && TaxDetail is null)
        {
            throw new DomainException("A tax leg needs its tax detail to be reversed.");
        }

        return new TransactionCashLeg
        {
            TenantId = TenantId,
            AccountId = AccountId,
            Role = Role,
            Amount = -Amount,
            Currency = Currency,
            FeeDetail = FeeDetail?.Copy(),
            TaxDetail = TaxDetail?.Copy()
        };
    }

    /// <summary>A <see cref="CashLegRole.Tax"/> leg with its tax detail (interest-run R25).</summary>
    internal static TransactionCashLeg CreateTax(
        int tenantId,
        int accountId,
        decimal amount,
        string currency,
        TransactionTaxDetail detail) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Role = CashLegRole.Tax,
            Amount = amount,
            Currency = currency,
            TaxDetail = detail
        };

    /// <summary>A <see cref="CashLegRole.Reinvestment"/> leg: the part of a dividend taken in new shares (dividends R40).</summary>
    internal static TransactionCashLeg CreateReinvestment(int tenantId, int accountId, decimal amount, string currency)
    {
        if (amount >= 0m)
        {
            throw new DomainException("A reinvestment leg is negative.");
        }

        return new TransactionCashLeg
        {
            TenantId = tenantId,
            AccountId = accountId,
            Role = CashLegRole.Reinvestment,
            Amount = amount,
            Currency = currency
        };
    }

    /// <summary>A <see cref="CashLegRole.Fee"/> leg with its fee detail (trade-fees R18).</summary>
    internal static TransactionCashLeg CreateFee(
        int tenantId,
        int accountId,
        decimal amount,
        string currency,
        TransactionFeeDetail detail) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Role = CashLegRole.Fee,
            Amount = amount,
            Currency = currency,
            FeeDetail = detail
        };
}

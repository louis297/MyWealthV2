using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

public class Account : BaseAuditableEntity
{
    private Account()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int CustomerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }

    /// <summary>Interest-run R19: the last UTC day this account was accrued through; null before the first time.</summary>
    public DateOnly? InterestAccruedThrough { get; private set; }

    /// <summary>Dividends R17: the standing election to reinvest dividends under a plan; only on a Custodial account.</summary>
    public bool ReinvestDividends { get; private set; }

    /// <summary>Dividends R17: who last changed the election, and when.</summary>
    public int? ReinvestDividendsChangedByUserId { get; private set; }

    public DateTimeOffset? ReinvestDividendsChangedAt { get; private set; }

    public CustodyMode CustodyMode { get; private set; }

    public AccountStatus Status { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>Set once on a Custodial account, never on an External one (client-money-accounts R14, R18).</summary>
    public DepositReference? DepositReference { get; private set; }

    public bool IsActive
    {
        get => Status == AccountStatus.Open;
        private set { }
    }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>Custody modes R3: a Custodial account is a Brokerage account.</summary>
    public const string CustodialTypeMessage = "A Custodial account must be a Brokerage account.";

    public static Account Create(
        int tenantId,
        int customerId,
        string name,
        AccountType type,
        CustodyMode custodyMode,
        Currency currency,
        DepositReference? depositReference = null,
        Guid? publicId = null)
    {
        EnsureCurrency(currency);
        if (custodyMode == CustodyMode.Custodial && type != AccountType.Brokerage)
        {
            throw new DomainException(CustodialTypeMessage);
        }

        // Client money accounts R14: a Custodial account has a deposit reference, an External account has none.
        if (custodyMode == CustodyMode.Custodial && depositReference is null)
        {
            throw new DomainException("A Custodial account needs a deposit reference.");
        }

        if (custodyMode == CustodyMode.External && depositReference is not null)
        {
            throw new DomainException("An External account has no deposit reference.");
        }

        var account = new Account
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Name = NormaliseName(name),
            Type = type,
            CustodyMode = custodyMode,
            Currency = currency.Code,
            DepositReference = depositReference,
            PublicId = publicId ?? Guid.NewGuid(),
            Status = AccountStatus.Open
        };
        account.AddDomainEvent(new AccountOpened(account));
        return account;
    }

    /// <summary>Interest-run R19: the account was accrued through <paramref name="through"/>. Only moves forward; only
    /// on a Custodial account.</summary>
    public void MarkInterestAccrued(DateOnly through)
    {
        if (CustodyMode != CustodyMode.Custodial)
        {
            throw new DomainException("Only a Custodial account is accrued.");
        }

        if (InterestAccruedThrough is { } current && through < current)
        {
            throw new DomainException("An account's interest is never accrued back to an earlier day.");
        }

        InterestAccruedThrough = through;
    }

    public void Rename(string name)
    {
        Name = NormaliseName(name);
    }

    public void Close()
    {
        if (Status == AccountStatus.Closed)
        {
            return;
        }

        Status = AccountStatus.Closed;
        AddDomainEvent(new AccountClosed(this));
    }

    public void Reopen()
    {
        if (Status == AccountStatus.Open)
        {
            return;
        }

        Status = AccountStatus.Open;
        AddDomainEvent(new AccountReopened(this));
    }

    private static string NormaliseName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 200)
        {
            throw new DomainException("Name is required.");
        }

        return trimmed;
    }

    private static void EnsureCurrency(Currency currency)
    {
        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new DomainException("Currency is required.");
        }
    }
}

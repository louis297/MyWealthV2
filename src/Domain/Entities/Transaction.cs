using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

public class Transaction : BaseAuditableEntity
{
    private Transaction()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    public DateTimeOffset BookedAt { get; private set; }

    public string? Memo { get; private set; }

    public string? Reference { get; private set; }

    public int? OriginalTransactionId { get; private set; }

    public int? ReversedByTransactionId { get; private set; }

    private Transaction? ReversedBy { get; set; }

    public byte[] RowVersion { get; private set; } = null!;

    public TransactionCashLeg? CashLeg { get; private set; }

    public List<TransactionSecurityLeg> SecurityLegs { get; private set; } = [];

    public static Transaction Post(
        int tenantId,
        int accountId,
        TransactionType type,
        decimal amount,
        string accountCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentCashSum,
        int existingTransactionCount)
    {
        EnsureSign(type, amount);
        if (type == TransactionType.Opening && existingTransactionCount != 0)
        {
            throw new DomainException("Opening is allowed only while the account has no other transaction.");
        }

        if (type == TransactionType.CloseOut && amount != -currentCashSum)
        {
            throw new DomainException("CloseOut must bring the cash balance to zero.");
        }

        if (currentCashSum + amount < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = type,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            CashLeg = TransactionCashLeg.Create(amount, currency)
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostOpeningHoldings(
        int tenantId,
        int accountId,
        AccountType accountType,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        IEnumerable<OpeningSecurityLeg> securityLegs)
    {
        if (accountType is not AccountType.Brokerage and not AccountType.Other)
        {
            throw new DomainException($"Account type {accountType.ToString()} is not allowed for holding securities.");
        }

        IEnumerable<OpeningSecurityLeg> openingSecurityLegs = securityLegs.ToList();
        if (!openingSecurityLegs.Any())
        {
            throw new DomainException("Empty security legs when creating OpeningHolding transaction");
        }

        if (openingSecurityLegs.Select(t => t.InstrumentId).Distinct().Count() != openingSecurityLegs.Count())
        {
            throw new DomainException("Duplicated instruments in OpeningHolding transaction.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TransactionType.OpeningHoldings,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            SecurityLegs = openingSecurityLegs.Select(t => TransactionSecurityLeg.Create(
                    t.InstrumentId, t.Quantity, t.Cost, t.QuoteCurrency
                    )).ToList()
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostBuy(
        int tenantId,
        int accountId,
        AccountType accountType,
        string accountCurrency,
        int instrumentId,
        decimal quantity,
        Money price,
        decimal fxRate,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentCashSum)
    {
        EnsureSecurityAccount(accountType);
        EnsurePriced(price);
        var cost = RoundMoney(quantity * price.Amount);
        var cash = -RoundMoney(cost * fxRate);
        if (currentCashSum + cash < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }
        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TransactionType.Buy,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            CashLeg = TransactionCashLeg.Create(cash, currency),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(instrumentId, quantity, cost, price.Currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostSell(
        int tenantId,
        int accountId,
        AccountType accountType,
        string accountCurrency,
        int instrumentId,
        decimal quantity,
        Money price,
        decimal fxRate,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentCashSum,
        decimal currentQuantity,
        decimal currentCost)
    {
        EnsureSecurityAccount(accountType);
        EnsurePriced(price);
        _ = currentCashSum;
        EnsureCanRelease(quantity, currentQuantity);
        var released = ReleaseCost(currentCost, quantity, currentQuantity);
        var cash = RoundMoney(quantity * price.Amount * fxRate);
        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TransactionType.Sell,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            CashLeg = TransactionCashLeg.Create(cash, currency),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(instrumentId, -quantity, -released, price.Currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostStockIn(
        int tenantId,
        int accountId,
        AccountType accountType,
        int instrumentId,
        decimal quantity,
        decimal cost,
        string quoteCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference)
    {
        EnsureSecurityAccount(accountType);
        if (cost < 0)
        {
            throw new DomainException("Cost must be greater than or equal to 0.");
        }

        var currency = Money.Create(0m, quoteCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TransactionType.StockIn,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(instrumentId, quantity, cost, currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostStockOut(
        int tenantId,
        int accountId,
        AccountType accountType,
        int instrumentId,
        decimal quantity,
        string quoteCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentQuantity,
        decimal currentCost)
    {
        EnsureSecurityAccount(accountType);
        EnsureCanRelease(quantity, currentQuantity);
        var released = ReleaseCost(currentCost, quantity, currentQuantity);
        var currency = Money.Create(0m, quoteCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TransactionType.StockOut,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(instrumentId, -quantity, -released, currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public static Transaction PostSplit(
        int tenantId,
        int accountId,
        AccountType accountType,
        TransactionType type,
        int instrumentId,
        decimal newQuantity,
        string quoteCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        decimal currentQuantity,
        decimal currentCost)
    {
        EnsureSecurityAccount(accountType);
        if (type is not TransactionType.Split and not TransactionType.Bonus and not TransactionType.Scrip)
        {
            throw new DomainException("Type must be Split, Bonus, or Scrip.");
        }

        _ = currentCost;
        if (currentQuantity <= 0)
        {
            throw new DomainException("A holding must already exist.");
        }

        if (newQuantity <= 0)
        {
            throw new DomainException("Quantity must be greater than 0.");
        }

        if (newQuantity == currentQuantity)
        {
            throw new DomainException("New quantity must differ from the current quantity.");
        }

        var currency = Money.Create(0m, quoteCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = type,
            BookedAt = bookedAt,
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(instrumentId, newQuantity - currentQuantity, 0m, currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    public Transaction Reverse(
        DateTimeOffset bookedAt,
        decimal currentCashSum,
        IReadOnlyDictionary<int, decimal>? currentQuantityByInstrument = null)
    {
        if (Type == TransactionType.Reversal)
        {
            throw new DomainException("A reversal cannot be reversed.");
        }

        if (ReversedByTransactionId is not null || ReversedBy is not null)
        {
            throw new DomainException("This transaction has already been reversed.");
        }

        TransactionCashLeg? cashLeg = null;
        if (CashLeg is not null)
        {
            var opposite = -CashLeg.Amount;
            if (currentCashSum + opposite < 0)
            {
                throw new DomainException("Cash balance cannot become negative.");
            }

            cashLeg = TransactionCashLeg.Create(opposite, CashLeg.Currency);
        }

        List<TransactionSecurityLeg>? reversalLegs = null;
        if (SecurityLegs.Count > 0)
        {
            reversalLegs = new List<TransactionSecurityLeg>(SecurityLegs.Count);
            foreach (var leg in SecurityLegs)
            {
                var current = leg.Quantity;
                if (currentQuantityByInstrument is not null
                    && !currentQuantityByInstrument.TryGetValue(leg.InstrumentId, out current))
                {
                    throw new DomainException("Holding quantity is required to reverse a security leg.");
                }

                var result = current - leg.Quantity;
                if (Type is TransactionType.Split or TransactionType.Bonus or TransactionType.Scrip)
                {
                    if (result <= 0)
                    {
                        throw new DomainException("Holding quantity must stay greater than zero.");
                    }
                }
                else if (result < 0)
                {
                    throw new DomainException("Holding quantity cannot become negative.");
                }

                reversalLegs.Add(TransactionSecurityLeg.Create(
                    leg.InstrumentId,
                    -leg.Quantity,
                    -leg.CostAmount,
                    leg.CostCurrency));
            }
        }

        if (cashLeg is null && reversalLegs is null)
        {
            throw new DomainException("A transaction has no cash leg to reverse.");
        }

        var reversal = new Transaction
        {
            TenantId = TenantId,
            AccountId = AccountId,
            Type = TransactionType.Reversal,
            BookedAt = bookedAt,
            PublicId = Guid.NewGuid(),
            OriginalTransactionId = Id,
            CashLeg = cashLeg,
            SecurityLegs = reversalLegs ?? []
        };
        ReversedBy = reversal;
        reversal.AddDomainEvent(new TransactionReversed(reversal));
        return reversal;
    }

    private static string? NormaliseMemo(string? memo)
    {
        if (memo is null)
        {
            return null;
        }

        var trimmed = memo.Trim();
        if (trimmed.Length is < 1 or > 200)
        {
            throw new DomainException("Memo must be 1 to 200 characters.");
        }

        return trimmed;
    }

    private static string? NormaliseReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var trimmed = reference.Trim();
        if (trimmed.Length > 100)
        {
            throw new DomainException("Reference must be at most 100 characters.");
        }

        return trimmed;
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

    private static decimal ReleaseCost(decimal oldCost, decimal sellQuantity, decimal oldQuantity) =>
        sellQuantity == oldQuantity
            ? oldCost
            : RoundMoney(oldCost * sellQuantity / oldQuantity);

    private static void EnsurePriced(Money price)
    {
        if (price.Amount <= 0)
        {
            throw new DomainException("Price must be greater than 0.");
        }
    }

    private static void EnsureSecurityAccount(AccountType accountType)
    {
        if (accountType is not AccountType.Brokerage and not AccountType.Other)
        {
            throw new DomainException($"Account type {accountType} is not allowed for holding securities.");
        }
    }

    private static void EnsureCanRelease(decimal quantity, decimal currentQuantity)
    {
        if (currentQuantity <= 0 || quantity > currentQuantity)
        {
            throw new DomainException("Quantity cannot exceed the holding quantity.");
        }
    }

    private static void EnsureSign(TransactionType type, decimal amount)
    {
        var invalid = type switch
        {
            TransactionType.TransferIn or TransactionType.Opening => amount <= 0,
            TransactionType.TransferOut or TransactionType.CloseOut => amount >= 0,
            TransactionType.Interest => amount == 0,
            _ => true
        };

        if (invalid)
        {
            throw new DomainException("Amount sign is not valid for this transaction type.");
        }
    }
}

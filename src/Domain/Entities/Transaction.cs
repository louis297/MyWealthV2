using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

public class Transaction : BaseAuditableEntity
{
    private Transaction()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public TransactionType Type { get; private set; }

    public DateTimeOffset BookedAt { get; private set; }

    public string? Memo { get; private set; }

    public string? Reference { get; private set; }

    public int? OriginalTransactionId { get; private set; }

    public int? ReversedByTransactionId { get; private set; }

    private Transaction? ReversedBy { get; set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The process that wrote this Transaction (transaction-source R2). Set by the factory or <see cref="Reverse"/>.</summary>
    public TransactionSource Source => TransactionSource.FromStored(SourceKind, SourcePublicId);

    /// <summary>Stored in <c>Transactions.SourceKind</c>; read through <see cref="Source"/>.</summary>
    public TransactionSourceKind SourceKind { get; private set; }

    /// <summary>Stored in <c>Transactions.SourcePublicId</c>; read through <see cref="Source"/>.</summary>
    public Guid? SourcePublicId { get; private set; }

    public List<TransactionCashLeg> CashLegs { get; private set; } = [];

    public List<TransactionSecurityLeg> SecurityLegs { get; private set; } = [];

    /// <summary>The rate this Transaction used, when it has one (FX conversions R14).</summary>
    public TransactionFxDetail? FxDetail { get; private set; }

    /// <summary>Why the adjustment was entered, on an <c>Adjustment</c> and its Reversal (adjustments §5).</summary>
    public TransactionAdjustmentDetail? AdjustmentDetail { get; private set; }

    /// <summary>The bank's details, on a deposit and on its Reversal (deposit-matching R32, R33).</summary>
    public TransactionBankReceiptDetail? BankReceiptDetail { get; private set; }

    /// <summary>The bank's details of a payout and of its Reversal (withdrawals R41).</summary>
    public TransactionPaymentDetail? PaymentDetail { get; private set; }

    /// <summary>One per <see cref="CashLegRole.Fee"/> cash leg (trade-fees R18).</summary>
    public List<TransactionFeeDetail> FeeDetails { get; private set; } = [];

    /// <summary>One per <see cref="CashLegRole.Tax"/> cash leg (interest-run R25).</summary>
    public List<TransactionTaxDetail> TaxDetails { get; private set; } = [];

    /// <summary>The period an interest posting pays, on the posting and its Reversal (interest-run R26).</summary>
    public TransactionInterestDetail? InterestDetail { get; private set; }

    /// <summary>The memo every interest posting carries (interest-run R23).</summary>
    public const string InterestMemo = "Interest";

    /// <summary>
    /// Interest-run R22–R26: an interest posting. One <c>Principal</c> <c>Interest</c> cash leg of
    /// <c>gross = Round(accrued, decimalPlaces)</c>; when the tax of <paramref name="withholding"/> on the gross is above
    /// 0, a <c>Tax</c> cash leg of <c>−tax</c> with its tax detail; an interest detail; source <c>InterestRun</c>. Built
    /// without the manual entry matrix (custody-modes R7).
    /// </summary>
    public static Transaction PostInterest(
        int tenantId,
        int accountId,
        string accountCurrency,
        int decimalPlaces,
        decimal accrued,
        Interest.InterestPeriod period,
        Interest.Withholding withholding,
        DateTimeOffset bookedAt,
        TransactionSource source) =>
        throw new NotImplementedException();

    public static Transaction Post(
        int tenantId,
        int accountId,
        TransactionType type,
        decimal amount,
        string accountCurrency,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        TransactionSource source,
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
            Type = type,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, amount, currency)]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>The memo every deposit carries (deposit-matching R32).</summary>
    public const string BankDepositMemo = "Bank deposit";

    /// <summary>
    /// A deposit from the bank statement (deposit-matching R32): one <c>TransferIn</c> cash leg, source
    /// <c>BankFeed</c>, memo <see cref="BankDepositMemo"/>, no reference, and the bank receipt detail. Flows build it
    /// without the manual entry matrix (custody-modes R7).
    /// </summary>
    public static Transaction PostBankDeposit(
        int tenantId,
        int accountId,
        string accountCurrency,
        decimal amount,
        DateTimeOffset bookedAt,
        TransactionSource source,
        BankReceipt receipt)
    {
        if (amount <= 0)
        {
            throw new DomainException("A deposit must be greater than zero.");
        }

        if (source.Kind != TransactionSourceKind.BankFeed)
        {
            throw new DomainException("A deposit comes from the bank feed.");
        }

        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.TransferIn,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = BankDepositMemo,
            Reference = null,
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, amount, currency)],
            BankReceiptDetail = TransactionBankReceiptDetail.Create(tenantId, receipt)
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>The memo every payout carries (withdrawals R41).</summary>
    public const string PayoutMemo = "Withdrawal";

    /// <summary>
    /// A withdrawal's payout from the bank statement (withdrawals R41): one <c>TransferOut</c> cash leg of
    /// <c>−amount</c>, source <c>Payment</c>, memo <see cref="PayoutMemo"/>, no reference, and the payment detail.
    /// Flows build it without the manual entry matrix (custody-modes R7).
    /// </summary>
    public static Transaction PostPayout(
        int tenantId,
        int accountId,
        string accountCurrency,
        decimal amount,
        DateTimeOffset bookedAt,
        TransactionSource source,
        BankPayment payment)
    {
        if (amount <= 0)
        {
            throw new DomainException("A payout must be greater than zero.");
        }

        if (source.Kind != TransactionSourceKind.Payment)
        {
            throw new DomainException("A payout comes from a payment.");
        }

        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.TransferOut,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = PayoutMemo,
            Reference = null,
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, -amount, currency)],
            PaymentDetail = TransactionPaymentDetail.Create(tenantId, payment)
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>The memo every remainder write-off carries (account-closure R18).</summary>
    public const string RemainderWriteOffMemo = "Account closure remainder";

    /// <summary>
    /// Account closure R18: the remainder write-off. One <c>CloseOut</c> <c>Principal</c> cash leg of
    /// <c>−cashSum</c>, which brings the cash <c>SUM</c> to exactly 0; source <c>AccountClosure</c>, memo
    /// <see cref="RemainderWriteOffMemo"/>, no reference. Only a remainder (<c>0 &lt; cashSum &lt; u</c>) is written off.
    /// The flow builds it without the manual entry matrix (custody-modes R7); it is exempt from the minor-unit rule
    /// (posting R8).
    /// </summary>
    public static Transaction PostRemainderWriteOff(
        int tenantId,
        int accountId,
        string accountCurrency,
        int decimalPlaces,
        decimal cashSum,
        DateTimeOffset bookedAt,
        TransactionSource source)
    {
        if (cashSum <= 0m || !ClosureAmounts.IsRemainder(cashSum, decimalPlaces))
        {
            throw new DomainException("Only a remainder smaller than the currency's minor unit is written off.");
        }

        if (source.Kind != TransactionSourceKind.AccountClosure)
        {
            throw new DomainException("A remainder write-off comes from an account closure.");
        }

        var currency = Money.Create(0m, accountCurrency).Currency;
        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.CloseOut,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = RemainderWriteOffMemo,
            Reference = null,
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, -cashSum, currency)]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>Custody modes I2: every leg account of one Transaction has the same custody mode.</summary>
    public const string DifferentCustodyModesMessage = "Both accounts must have the same custody mode.";

    private static void EnsureSameCustodyMode(CustodyMode fromCustodyMode, CustodyMode toCustodyMode)
    {
        if (fromCustodyMode != toCustodyMode)
        {
            throw new DomainException(DifferentCustodyModesMessage);
        }
    }

    /// <summary>
    /// Builds an InternalTransfer: a negative cash leg on the from Account and a positive cash leg
    /// on the to Account, same currency, summing to zero.
    /// </summary>
    public static Transaction PostInternalTransfer(
        int tenantId,
        int fromAccountId,
        string fromAccountCurrency,
        CustodyMode fromCustodyMode,
        int toAccountId,
        string toAccountCurrency,
        CustodyMode toCustodyMode,
        Money amount,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        TransactionSource source,
        decimal fromCurrentCashSum)
    {
        if (fromAccountId == toAccountId)
        {
            throw new DomainException("The from and to accounts must be different.");
        }

        EnsureSameCustodyMode(fromCustodyMode, toCustodyMode);

        var currency = Money.Create(0m, fromAccountCurrency).Currency;
        if (!string.Equals(currency, Money.Create(0m, toAccountCurrency).Currency, StringComparison.Ordinal))
        {
            throw new DomainException("The from and to accounts must have the same currency.");
        }

        if (!string.Equals(amount.Currency, currency, StringComparison.Ordinal))
        {
            throw new DomainException("The amount currency must match the account currency.");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException("Amount must be greater than 0.");
        }

        if (fromCurrentCashSum - amount.Amount < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.InternalTransfer,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs =
            [
                TransactionCashLeg.Create(tenantId, fromAccountId, -amount.Amount, currency),
                TransactionCashLeg.Create(tenantId, toAccountId, amount.Amount, currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>
    /// Builds an FxConversion: a negative cash leg in the sell currency on the from Account and a
    /// positive cash leg in the buy currency on the to Account, priced at <paramref name="rate"/>.
    /// <paramref name="fixedAmount"/> is the side the caller named; its currency says which side it is.
    /// </summary>
    public static Transaction PostFxConversion(
        int tenantId,
        int fromAccountId,
        string fromAccountCurrency,
        CustodyMode fromCustodyMode,
        int toAccountId,
        string toAccountCurrency,
        CustodyMode toCustodyMode,
        AppliedFxRate rate,
        Money fixedAmount,
        int computedDecimalPlaces,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        TransactionSource source,
        decimal fromCurrentCashSum)
    {
        if (fromAccountId == toAccountId)
        {
            throw new DomainException("The from and to accounts must be different.");
        }

        EnsureSameCustodyMode(fromCustodyMode, toCustodyMode);

        var sellCurrency = Money.Create(0m, fromAccountCurrency).Currency;
        var buyCurrency = Money.Create(0m, toAccountCurrency).Currency;
        if (string.Equals(sellCurrency, buyCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The from and to accounts must have different currencies.");
        }

        if (!string.Equals(rate.BaseCurrency, sellCurrency, StringComparison.Ordinal)
            || !string.Equals(rate.QuoteCurrency, buyCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The rate must be for the sell currency to the buy currency.");
        }

        var (sell, buy) = ComputeFxConversionAmounts(rate, fixedAmount, computedDecimalPlaces);

        if (fromCurrentCashSum - sell < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.FxConversion,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs =
            [
                TransactionCashLeg.Create(tenantId, fromAccountId, -sell, sellCurrency),
                TransactionCashLeg.Create(tenantId, toAccountId, buy, buyCurrency)
            ],
            FxDetail = TransactionFxDetail.Create(tenantId, rate)
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>
    /// External caller numbers R17, R18: an FxConversion from both amounts as the caller entered them.
    /// </summary>
    public static Transaction PostFxConversionWithAmounts(
        int tenantId,
        int fromAccountId,
        string fromAccountCurrency,
        CustodyMode fromCustodyMode,
        int toAccountId,
        string toAccountCurrency,
        CustodyMode toCustodyMode,
        Money sell,
        Money buy,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        TransactionSource source,
        decimal fromCurrentCashSum)
    {
        if (fromAccountId == toAccountId)
        {
            throw new DomainException("The from and to accounts must be different.");
        }

        EnsureSameCustodyMode(fromCustodyMode, toCustodyMode);

        var sellCurrency = Money.Create(0m, fromAccountCurrency).Currency;
        var buyCurrency = Money.Create(0m, toAccountCurrency).Currency;
        if (string.Equals(sellCurrency, buyCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The from and to accounts must have different currencies.");
        }

        if (!string.Equals(sell.Currency, sellCurrency, StringComparison.Ordinal)
            || !string.Equals(buy.Currency, buyCurrency, StringComparison.Ordinal))
        {
            throw new DomainException(
                "The sell amount must be in the from account's currency and the buy amount in the to account's currency.");
        }

        if (sell.Amount <= 0 || buy.Amount <= 0)
        {
            throw new DomainException("Amount must be greater than 0.");
        }

        var rate = FxRateFromAmounts(sell.Amount, buy.Amount);
        if (!FitsStoredFxRate(rate))
        {
            throw new DomainException(FxRateFromAmountsOutOfRangeMessage);
        }

        if (fromCurrentCashSum - sell.Amount < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var applied = AppliedFxRate.Create(
            sellCurrency, buyCurrency, rate, DateOnly.FromDateTime(bookedAt.UtcDateTime), AppliedFxRate.CallerSource);
        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.FxConversion,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs =
            [
                TransactionCashLeg.Create(tenantId, fromAccountId, -sell.Amount, sellCurrency),
                TransactionCashLeg.Create(tenantId, toAccountId, buy.Amount, buyCurrency)
            ],
            FxDetail = TransactionFxDetail.Create(tenantId, applied)
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>The largest rate <c>TransactionFxDetails.Rate</c> (<c>decimal(18,8)</c>) holds.</summary>
    public const decimal MaxStoredFxRate = 9_999_999_999.99999999m;

    public const string FxRateFromAmountsOutOfRangeMessage =
        "The rate from the two amounts must be greater than 0 and below 10,000,000,000.";

    /// <summary>External caller numbers R17: buy currency per 1 sell currency, rounded to 8 decimals away from zero.</summary>
    public static decimal FxRateFromAmounts(decimal sellAmount, decimal buyAmount) =>
        decimal.Round(buyAmount / sellAmount, 8, MidpointRounding.AwayFromZero);

    /// <summary>A rate above 0 that fits <c>TransactionFxDetails.Rate</c>.</summary>
    public static bool FitsStoredFxRate(decimal rate) => rate > 0 && rate <= MaxStoredFxRate;

    /// <summary>
    /// FX conversions R11: the sell and buy amounts for <paramref name="fixedAmount"/> at <paramref name="rate"/>.
    /// The fixed side is the one in its currency; the other side is rounded to
    /// <paramref name="computedDecimalPlaces"/>, away from zero.
    /// </summary>
    public static (decimal Sell, decimal Buy) ComputeFxConversionAmounts(
        AppliedFxRate rate,
        Money fixedAmount,
        int computedDecimalPlaces)
    {
        if (fixedAmount.Amount <= 0)
        {
            throw new DomainException("Amount must be greater than 0.");
        }

        decimal sell;
        decimal buy;
        if (string.Equals(fixedAmount.Currency, rate.BaseCurrency, StringComparison.Ordinal))
        {
            sell = fixedAmount.Amount;
            buy = decimal.Round(sell * rate.Rate, computedDecimalPlaces, MidpointRounding.AwayFromZero);
        }
        else if (string.Equals(fixedAmount.Currency, rate.QuoteCurrency, StringComparison.Ordinal))
        {
            buy = fixedAmount.Amount;
            sell = decimal.Round(buy / rate.Rate, computedDecimalPlaces, MidpointRounding.AwayFromZero);
        }
        else
        {
            throw new DomainException("The amount currency must be the sell or the buy currency.");
        }

        if (sell == 0 || buy == 0)
        {
            throw new DomainException("The computed amount rounds to 0.");
        }

        return (sell, buy);
    }

    public static Transaction PostOpeningHoldings(
        int tenantId,
        int accountId,
        AccountType accountType,
        DateTimeOffset bookedAt,
        string? memo,
        string? reference,
        TransactionSource source,
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
            Type = TransactionType.OpeningHoldings,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            SecurityLegs = openingSecurityLegs.Select(t => TransactionSecurityLeg.Create(
                    tenantId, accountId, t.InstrumentId, t.Quantity, t.Cost, t.QuoteCurrency
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
        TransactionSource source,
        decimal currentCashSum,
        TradeFeeSchedule? feeSchedule = null,
        int quoteDecimalPlaces = 2,
        decimal? enteredFee = null,
        AppliedFxRate? appliedRate = null)
    {
        EnsureSecurityAccount(accountType);
        EnsurePriced(price);
        var currency = Money.Create(0m, accountCurrency).Currency;
        EnsureTradeRate(appliedRate, price.Currency, currency, fxRate);
        var cost = RoundMoney(quantity * price.Amount);
        var cash = -RoundMoney(cost * fxRate);
        var brokerage = TradeFee(feeSchedule, enteredFee, cost, price.Currency, fxRate, quoteDecimalPlaces);
        if (currentCashSum + cash + (brokerage?.Leg ?? 0m) < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.Buy,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, cash, currency)],
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(
                    tenantId, accountId, instrumentId, quantity, cost + (brokerage?.Fee ?? 0m), price.Currency)
            ],
            FxDetail = appliedRate is null ? null : TransactionFxDetail.Create(tenantId, appliedRate)
        };
        transaction.AddBrokerageLeg(accountId, currency, brokerage);
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
        TransactionSource source,
        decimal currentCashSum,
        decimal currentQuantity,
        decimal currentCost,
        TradeFeeSchedule? feeSchedule = null,
        int quoteDecimalPlaces = 2,
        decimal? enteredFee = null,
        AppliedFxRate? appliedRate = null)
    {
        EnsureSecurityAccount(accountType);
        EnsurePriced(price);
        EnsureCanRelease(quantity, currentQuantity);
        var currency = Money.Create(0m, accountCurrency).Currency;
        EnsureTradeRate(appliedRate, price.Currency, currency, fxRate);
        var released = ReleaseCost(currentCost, quantity, currentQuantity);
        var cash = RoundMoney(quantity * price.Amount * fxRate);
        var brokerage = TradeFee(
            feeSchedule, enteredFee, RoundMoney(quantity * price.Amount), price.Currency, fxRate, quoteDecimalPlaces);
        if (brokerage is not null && currentCashSum + cash + brokerage.Leg < 0)
        {
            throw new DomainException("Cash balance cannot become negative.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.Sell,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            CashLegs = [TransactionCashLeg.Create(tenantId, accountId, cash, currency)],
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(tenantId, accountId, instrumentId, -quantity, -released, price.Currency)
            ],
            FxDetail = appliedRate is null ? null : TransactionFxDetail.Create(tenantId, appliedRate)
        };
        transaction.AddBrokerageLeg(accountId, currency, brokerage);
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
        string? reference,
        TransactionSource source)
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
            Type = TransactionType.StockIn,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(tenantId, accountId, instrumentId, quantity, cost, currency)
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
        TransactionSource source,
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
            Type = TransactionType.StockOut,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(tenantId, accountId, instrumentId, -quantity, -released, currency)
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
        TransactionSource source,
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
            Type = type,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            SecurityLegs =
            [
                TransactionSecurityLeg.Create(tenantId, accountId, instrumentId, newQuantity - currentQuantity, 0m, currency)
            ]
        };
        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>Adjustments R11: the <c>errors.Security</c> message when the holding result is refused.</summary>
    public const string AdjustmentHoldingMessage =
        "The holding cannot have a negative quantity or cost, or a cost without a quantity.";

    /// <summary>
    /// Builds an <c>Adjustment</c> on one Account (adjustments §5): an optional signed cash leg, an optional signed
    /// security leg stored as entered, at least one of them, and the adjustment detail.
    /// </summary>
    public static Transaction PostAdjustment(
        int tenantId,
        int accountId,
        AccountType accountType,
        string accountCurrency,
        AdjustmentReason reason,
        string? memo,
        string? reference,
        DateTimeOffset bookedAt,
        TransactionSource source,
        decimal? amount,
        decimal currentCashSum,
        AdjustmentHoldingChange? security)
    {
        if (amount is null && security is null)
        {
            throw new DomainException("At least one of amount and security is required.");
        }

        if (string.IsNullOrWhiteSpace(memo))
        {
            throw new DomainException("Memo is required on an adjustment.");
        }

        var transaction = new Transaction
        {
            TenantId = tenantId,
            Type = TransactionType.Adjustment,
            BookedAt = bookedAt.ToUniversalTime(),
            Memo = NormaliseMemo(memo),
            Reference = NormaliseReference(reference),
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            PublicId = Guid.NewGuid(),
            AdjustmentDetail = TransactionAdjustmentDetail.Create(tenantId, reason)
        };

        if (amount is { } cash)
        {
            if (cash == 0m)
            {
                throw new DomainException("Amount cannot be 0.");
            }

            if (currentCashSum + cash < 0)
            {
                throw new DomainException("Cash balance cannot become negative.");
            }

            var currency = Money.Create(0m, accountCurrency).Currency;
            transaction.CashLegs.Add(TransactionCashLeg.Create(tenantId, accountId, cash, currency));
        }

        if (security is not null)
        {
            EnsureSecurityAccount(accountType);
            if (security.QuantityChange == 0m && security.CostChange == 0m)
            {
                throw new DomainException("At least one of quantityChange and costChange must be non-zero.");
            }

            // R11: never below zero quantity or cost, never cost on a zero quantity (the row is deleted at zero).
            var quantity = security.CurrentQuantity + security.QuantityChange;
            var cost = security.CurrentCost + security.CostChange;
            if (quantity < 0 || cost < 0 || (quantity == 0 && cost != 0))
            {
                throw new DomainException(AdjustmentHoldingMessage);
            }

            var costCurrency = Money.Create(0m, security.QuoteCurrency).Currency;
            transaction.SecurityLegs.Add(TransactionSecurityLeg.Create(
                tenantId, accountId, security.InstrumentId, security.QuantityChange, security.CostChange, costCurrency));
        }

        transaction.AddDomainEvent(new TransactionPosted(transaction));
        return transaction;
    }

    /// <summary>
    /// Builds the Reversal: one opposite leg per original leg, each on that leg's Account.
    /// Balances are keyed by leg Account (and Instrument for holdings).
    /// <paramref name="hasLaterSecurityActivity"/> is true when a live later security leg exists on one of
    /// this Transaction's holdings (fixes/ledger-write-guards.md Decision 1).
    /// </summary>
    public Transaction Reverse(
        TransactionSource source,
        DateTimeOffset bookedAt,
        IReadOnlyDictionary<int, decimal> currentCashSumByAccount,
        IReadOnlyDictionary<(int AccountId, int InstrumentId), decimal>? currentQuantity = null,
        bool hasLaterSecurityActivity = false)
    {
        if (Type == TransactionType.Reversal)
        {
            throw new DomainException("A reversal cannot be reversed.");
        }

        if (ReversedByTransactionId is not null || ReversedBy is not null)
        {
            throw new DomainException("This transaction has already been reversed.");
        }

        // The cost a later Sell or StockOut released depends on this leg, so only the latest
        // security activity on a holding can be reversed (fixes/ledger-write-guards.md Decision 1).
        if (SecurityLegs.Count > 0 && hasLaterSecurityActivity)
        {
            throw new DomainException("A later transaction changed this holding. Reverse the later transactions first.");
        }

        // Roles and fee details are copied; the schedule is not read again (trade-fees R19).
        var cashLegs = CashLegs.Select(leg => leg.ReversalLeg()).ToList();
        foreach (var account in cashLegs.GroupBy(leg => leg.AccountId))
        {
            if (!currentCashSumByAccount.TryGetValue(account.Key, out var currentCashSum))
            {
                throw new DomainException("Cash balance is required for every leg account.");
            }

            if (currentCashSum + account.Sum(leg => leg.Amount) < 0)
            {
                throw new DomainException("Cash balance cannot become negative.");
            }
        }

        List<TransactionSecurityLeg>? reversalLegs = null;
        if (SecurityLegs.Count > 0)
        {
            reversalLegs = new List<TransactionSecurityLeg>(SecurityLegs.Count);
            foreach (var leg in SecurityLegs)
            {
                var current = leg.Quantity;
                if (currentQuantity is not null
                    && !currentQuantity.TryGetValue((leg.AccountId, leg.InstrumentId), out current))
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
                    TenantId,
                    leg.AccountId,
                    leg.InstrumentId,
                    -leg.Quantity,
                    -leg.CostAmount,
                    leg.CostCurrency));
            }
        }

        if (cashLegs.Count == 0 && reversalLegs is null)
        {
            throw new DomainException("A transaction has no cash leg to reverse.");
        }

        var reversal = new Transaction
        {
            TenantId = TenantId,
            Type = TransactionType.Reversal,
            BookedAt = bookedAt.ToUniversalTime(),
            PublicId = Guid.NewGuid(),
            OriginalTransactionId = Id,
            SourceKind = source.Kind,
            SourcePublicId = source.PublicId,
            CashLegs = cashLegs,
            SecurityLegs = reversalLegs ?? [],
            FxDetail = FxDetail?.Copy(),
            AdjustmentDetail = AdjustmentDetail?.Copy(),
            BankReceiptDetail = BankReceiptDetail?.Copy(),
            PaymentDetail = PaymentDetail?.Copy(),
            FeeDetails = cashLegs.Select(leg => leg.FeeDetail).OfType<TransactionFeeDetail>().ToList()
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

    /// <summary>
    /// The brokerage on a <c>Buy</c> / <c>Sell</c> (trade-fees R8, R14, R15): the fee in the quote currency and the
    /// negative fee leg in the Account currency, converted with the principal's rate and rounding. Null when there is
    /// no schedule or the fee is 0.
    /// </summary>
    private static BrokerageCharge? BrokerageFee(
        TradeFeeSchedule? schedule,
        decimal gross,
        string quoteCurrency,
        decimal fxRate,
        int quoteDecimalPlaces)
    {
        if (schedule is null)
        {
            return null;
        }

        if (!string.Equals(schedule.QuoteCurrency, quoteCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The trade fee schedule is for another quote currency.");
        }

        var fee = schedule.FeeFor(gross, quoteDecimalPlaces).Fee;
        var leg = -RoundMoney(fee * fxRate);
        return fee > 0m && leg < 0m ? new BrokerageCharge(schedule.Id, gross, fee, quoteCurrency, leg) : null;
    }

    /// <summary>
    /// The cash a <c>Buy</c> / <c>Sell</c> would book on its Account: principal plus the brokerage leg, with the same
    /// rounding as <see cref="PostBuy"/> and <see cref="PostSell"/>. The rebalance planner uses it (trade-fees R25).
    /// </summary>
    internal static decimal TradeCash(
        bool buy,
        decimal quantity,
        decimal price,
        string quoteCurrency,
        decimal fxRate,
        TradeFeeSchedule? feeSchedule,
        int quoteDecimalPlaces)
    {
        var gross = RoundMoney(quantity * price);
        var principal = buy ? -RoundMoney(gross * fxRate) : RoundMoney(quantity * price * fxRate);
        return principal + (BrokerageFee(feeSchedule, gross, quoteCurrency, fxRate, quoteDecimalPlaces)?.Leg ?? 0m);
    }

    private void AddBrokerageLeg(int accountId, string accountCurrency, BrokerageCharge? brokerage)
    {
        if (brokerage is null)
        {
            return;
        }

        var detail = TransactionFeeDetail.Create(
            TenantId, FeeKind.Brokerage, brokerage.ScheduleId, brokerage.Gross, brokerage.Fee, brokerage.QuoteCurrency);
        CashLegs.Add(TransactionCashLeg.CreateFee(TenantId, accountId, brokerage.Leg, accountCurrency, detail));
        FeeDetails.Add(detail);
    }

    /// <summary>
    /// The trade's fee: from the schedule the server read, or entered by the caller (external-caller-numbers R6),
    /// never both. None when neither is given or the fee rounds to 0.
    /// </summary>
    private static BrokerageCharge? TradeFee(
        TradeFeeSchedule? schedule,
        decimal? enteredFee,
        decimal gross,
        string quoteCurrency,
        decimal fxRate,
        int quoteDecimalPlaces)
    {
        if (enteredFee is null)
        {
            return BrokerageFee(schedule, gross, quoteCurrency, fxRate, quoteDecimalPlaces);
        }

        if (schedule is not null)
        {
            throw new DomainException("A trade fee comes from a trade fee schedule or is entered, not both.");
        }

        if (enteredFee.Value < 0)
        {
            throw new DomainException("Fee cannot be negative.");
        }

        var leg = -RoundMoney(enteredFee.Value * fxRate);
        return enteredFee.Value > 0m && leg < 0m
            ? new BrokerageCharge(null, gross, enteredFee.Value, quoteCurrency, leg)
            : null;
    }

    /// <summary>
    /// External caller numbers I2: a trade's FX detail is for its price currency to its account currency, at the rate
    /// its cash legs use.
    /// </summary>
    private static void EnsureTradeRate(AppliedFxRate? rate, string priceCurrency, string accountCurrency, decimal fxRate)
    {
        if (rate is null)
        {
            return;
        }

        if (!string.Equals(rate.BaseCurrency, priceCurrency, StringComparison.Ordinal)
            || !string.Equals(rate.QuoteCurrency, accountCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The rate must be for the price currency to the account currency.");
        }

        if (rate.Rate != fxRate)
        {
            throw new DomainException("The rate must be the one the cash legs use.");
        }
    }

    private sealed record BrokerageCharge(int? ScheduleId, decimal Gross, decimal Fee, string QuoteCurrency, decimal Leg);

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

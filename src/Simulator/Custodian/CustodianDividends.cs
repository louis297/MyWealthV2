using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>The status of one dividend payment of the simulator custodian (dividends R56, R59, R60).</summary>
public enum CustodianDividendPaymentStatus
{
    Due = 0,
    Held = 1,
    Paid = 2
}

/// <summary>
/// What the simulator custodian owes one of its accounts for one dividend (dividends R56): the entitled position when
/// the dividend went ex, the election the platform sent for it, and what it paid. Its own books: it never learns which
/// Account is entitled (R64).
/// </summary>
public sealed class CustodianDividendPayment
{
    private CustodianDividendPayment()
    {
    }

    public int Id { get; private set; }

    public int ActionId { get; private set; }

    public int AccountId { get; private set; }

    public decimal EntitledPosition { get; private set; }

    public string? ElectionInstructionId { get; private set; }

    public string? ElectionBody { get; private set; }

    public decimal? ElectedQuantity { get; private set; }

    public decimal? Shares { get; private set; }

    public decimal? Cash { get; private set; }

    public CustodianDividendPaymentStatus Status { get; private set; }

    public long? BankLineId { get; private set; }

    public int? MovementId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static CustodianDividendPayment Due(int actionId, int accountId, decimal entitledPosition) => new()
    {
        ActionId = actionId,
        AccountId = accountId,
        EntitledPosition = entitledPosition,
        Status = CustodianDividendPaymentStatus.Due
    };

    /// <summary>R58: an accepted election, stored with its instruction id and body.</summary>
    public void Elect(string instructionId, string body, decimal quantity)
    {
        ElectionInstructionId = instructionId;
        ElectionBody = body;
        ElectedQuantity = quantity;
    }

    /// <summary>R60: held for the operator in <c>Hold</c> mode.</summary>
    public void Hold() => Status = CustodianDividendPaymentStatus.Held;

    /// <summary>R59: paid, with the shares delivered and the cash, and the bank line and movement they made.</summary>
    public void Pay(decimal? shares, decimal cash, long? bankLineId, int? movementId)
    {
        Shares = shares;
        Cash = cash;
        BankLineId = bankLineId;
        MovementId = movementId;
        Status = CustodianDividendPaymentStatus.Paid;
    }
}

/// <summary>Dividends R56, R59, R60: a dividend at the custodian, when it goes ex and when it is paid.</summary>
public static class CustodianDividends
{
    /// <summary>
    /// R59, R60: on or after the payment date of an <c>Effective</c> dividend, each <c>Due</c> payment is paid, or
    /// held in <c>Hold</c> mode; the dividend is then marked paid on the simulator day. The caller holds the
    /// custodian's lock and commits.
    /// </summary>
    public static async Task PayDueAsync(SimulatorDbContext db, TimeProvider time, DateOnly today, CancellationToken cancellationToken)
    {
        var dividends = await db.MarketCorporateActions
            .Where(action => action.Kind == MarketCorporateActionKind.Dividend
                             && action.Status == MarketCorporateActionStatus.Effective
                             && action.CustodianApplied
                             && action.PaidOn == null
                             && action.PaymentDate <= today)
            .OrderBy(action => action.PaymentDate)
            .ThenBy(action => action.Number)
            .ToListAsync(cancellationToken);
        var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
        foreach (var dividend in dividends)
        {
            var payments = await db.CustodianDividendPayments
                .Where(row => row.ActionId == dividend.Id && row.Status == CustodianDividendPaymentStatus.Due)
                .OrderBy(row => row.Id)
                .ToListAsync(cancellationToken);
            foreach (var payment in payments)
            {
                if (settings.IncomeMode == CustodianIncomeMode.Hold)
                {
                    payment.Hold();
                }
                else
                {
                    await PayAsync(db, time, dividend, payment, null, cancellationToken);
                }
            }

            dividend.MarkPaid(today);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// R59, R60: pays one payment. Under an accepted election the custodian reinvests
    /// <c>Round(electedQuantity × netPerShare, 2)</c> in shares at the plan price, rounded by its rounding mode, and adds
    /// one <c>CorporateAction</c> movement in; the cash is <c>Round(entitledPosition × netPerShare, 2)</c> less the
    /// shares' value, or <paramref name="amount"/> in place of it. With cash above 0 and an income account open at the simulator bank, one credit line there with
    /// the account's name as payer and the party action id as reference. Saves.
    /// </summary>
    public static async Task PayAsync(
        SimulatorDbContext db,
        TimeProvider time,
        MarketCorporateAction dividend,
        CustodianDividendPayment payment,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        var terms = dividend.Terms!;
        var account = await db.CustodianAccounts.AsNoTracking().SingleAsync(row => row.Id == payment.AccountId, cancellationToken);
        var world = await db.Worlds.SingleAsync(cancellationToken);
        var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
        var shares = 0m;
        if (payment.ElectedQuantity is { } elected && terms.ReinvestmentPrice is { } price)
        {
            var reinvest = decimal.Round(elected * terms.NetPerShare, 2, MidpointRounding.AwayFromZero);
            var exact = reinvest / price;
            shares = settings.RoundingMode == CustodianRoundingMode.WholeShares
                ? decimal.Truncate(exact)
                : decimal.Truncate(exact * 100_000_000m) / 100_000_000m;
        }

        var sharesValue = decimal.Round(shares * (terms.ReinvestmentPrice ?? 0m), 2, MidpointRounding.AwayFromZero);
        var cash = amount ?? decimal.Round(payment.EntitledPosition * terms.NetPerShare, 2, MidpointRounding.AwayFromZero) - sharesValue;
        var day = Market.MarketPricePath.Day(world.Now(time.GetUtcNow()));
        CustodianMovement? movement = null;
        var symbol = await db.MarketInstruments.AsNoTracking()
            .Where(row => row.Id == dividend.InstrumentId)
            .Select(row => row.Symbol)
            .SingleAsync(cancellationToken);
        if (shares > 0m)
        {
            movement = await CustodianBook.AddAsync(db, account.AccountNumber, CustodianMovement.InDirection,
                CustodianMovementKind.CorporateAction, symbol, account.Currency.Trim(), shares, day, DateTimeOffset.UtcNow,
                corporateActionId: MarketCorporateAction.ActionId(world.WorldId, dividend.Number), cancellationToken: cancellationToken);
        }

        Bank.BankLine? line = null;
        var currency = account.Currency.Trim();
        var income = account.IncomeAccountNumber is null
            ? null
            : await db.BankAccounts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountNumber == account.IncomeAccountNumber && row.Currency == currency, cancellationToken);
        if (cash > 0m && income is not null)
        {
            var simulatorNow = world.Now(time.GetUtcNow());
            line = Bank.BankLine.Receipt(
                world.TakeLineOutsideId(), income.Id, cash, Market.MarketPricePath.Day(simulatorNow), account.Name, account.AccountNumber,
                MarketCorporateAction.ActionId(world.WorldId, dividend.Number), simulatorNow, simulatorNow, deliverTwice: false);
            db.BankLines.Add(line);
            await db.SaveChangesAsync(cancellationToken);
        }

        payment.Pay(shares > 0m ? shares : null, cash, line?.Id, movement?.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>One <c>Due</c> payment per account whose position in <paramref name="symbol"/> is above 0, with that
    /// position. No movement: a dividend changes no position.</summary>
    public static async Task GoExAsync(SimulatorDbContext db, MarketCorporateAction action, string symbol, CancellationToken cancellationToken)
    {
        var accounts = await db.CustodianAccounts.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var position = (await CustodianBook.PositionsAsync(db, account.AccountNumber, null, cancellationToken))
                .Where(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Quantity);
            if (position > 0m)
            {
                db.CustodianDividendPayments.Add(CustodianDividendPayment.Due(action.Id, account.Id, position));
            }
        }
    }
}

/// <summary>Dividends R58: the first answer of a rejected election, kept so the same instruction gets it again.</summary>
public sealed class CustodianRejectedElection
{
    private CustodianRejectedElection()
    {
    }

    public int Id { get; private set; }

    public int AccountId { get; private set; }

    public string InstructionId { get; private set; } = "";

    public string Body { get; private set; } = "";

    public string Reason { get; private set; } = "";

    public static CustodianRejectedElection Create(int accountId, string instructionId, string body, string reason) => new()
    {
        AccountId = accountId,
        InstructionId = instructionId,
        Body = body,
        Reason = reason
    };
}

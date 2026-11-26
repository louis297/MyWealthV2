using System.Globalization;
using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public sealed class AdviserBalancesReportGenerator(
    IApplicationDbContext db,
    ICurrencyCatalog currencies,
    IMarketData marketData,
    IValueAccount valueAccount,
    TimeProvider time) : IReportGenerator
{
    public string Code => AdviserBalancesBinder.Code;

    public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        if (execution.AsOf is null)
        {
            throw new InvalidOperationException("As of is required.");
        }

        using var parameters = JsonDocument.Parse(execution.ParametersJson);
        var binding = AdviserBalancesBinder.Bind(
            parameters.RootElement, UserRole.TenantAdmin, callerPublicId: null, AdviserBalancesBindMode.Snapshot);
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.Id == execution.TenantId, cancellationToken)
                     ?? throw new InvalidOperationException("Tenant was not found.");
        var adviser = await db.Users.AsNoTracking()
                          .SingleOrDefaultAsync(
                              row => row.PublicId == binding.AdviserId
                                     && row.TenantId == execution.TenantId
                                     && row.Role == UserRole.Adviser,
                              cancellationToken)
                      ?? throw new InvalidOperationException("Adviser is not in scope.");

        if (binding.Date is { } date)
        {
            return await GeneratePastAsync(execution, tenant, adviser, date, cancellationToken);
        }

        var accounts = await (
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.TenantId == execution.TenantId
                  && customer.Role == UserRole.Customer
                  && customer.AdviserId == adviser.Id
            select new AccountRow(
                account.Id,
                account.PublicId,
                account.Name,
                account.Type,
                account.CustodyMode,
                account.Status,
                account.Currency,
                customer.PublicId,
                customer.Name)
        ).ToListAsync(cancellationToken);

        var accountIds = accounts.Select(account => account.Id).ToList();
        var cashByAccount = accountIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await (
                from leg in db.TransactionCashLegs.AsNoTracking()
                where accountIds.Contains(leg.AccountId)
                group leg by leg.AccountId
                into grouped
                select new { AccountId = grouped.Key, Sum = grouped.Sum(leg => leg.Amount) }
            ).ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);

        var holdingRows = accountIds.Count == 0
            ? []
            : await (
                from holding in db.Holdings.AsNoTracking()
                join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
                where accountIds.Contains(holding.AccountId)
                select new HoldingRow(
                    holding.AccountId,
                    instrument.PublicId,
                    instrument.Id,
                    instrument.Symbol,
                    holding.Quantity,
                    holding.CostAmount,
                    holding.CostCurrency,
                    instrument.QuoteCurrency)
            ).ToListAsync(cancellationToken);

        var holdingsByAccount = holdingRows
            .GroupBy(row => row.AccountId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var sources = accounts.Select(account => new AdviserBalanceAccount(
            account.CustomerId,
            account.CustomerName,
            account.AccountId,
            account.AccountName,
            account.Type.ToString(),
            account.CustodyMode.ToString(),
            account.Currency.Trim(),
            cashByAccount.GetValueOrDefault(account.Id),
            account.Status == AccountStatus.Closed,
            holdingsByAccount.TryGetValue(account.Id, out var holdings)
                ? holdings.Select(ToHolding).ToList()
                : [])).ToList();

        var selection = AdviserBalancesRows.Select(sources, binding.IncludeClosedAccounts);
        var rows = selection.Lines.Select(ToPdfRow).ToList();
        var subtotals = selection.Subtotals.Select(ToPdfSubtotal).ToList();
        var modeSubtotals = selection.ModeSubtotals
            .Select(block => new AdviserBalancesPdfSubtotalBlock(
                block.CustodyMode,
                block.Subtotals.Select(ToPdfSubtotal).ToList()))
            .ToList();

        return AdviserBalancesPdf.Render(new AdviserBalancesPdfModel(
            tenant.Name,
            adviser.Name,
            execution.AsOf.Value,
            time.GetUtcNow(),
            binding.IncludeClosedAccounts,
            rows,
            subtotals,
            modeSubtotals));
    }

    /// <summary>
    /// Valuation-history R17–R19: every account of the Adviser's Customers, of either custody mode and any status, at the
    /// end of <paramref name="date"/>. A Custodial account is read from its stored valuation of the day (or is "Not
    /// valued yet" when it is expected on the day and has none); an External account is valued on read from the
    /// ledger as it stands now.
    /// </summary>
    private async Task<byte[]> GeneratePastAsync(
        ReportExecution execution, Tenant tenant, User adviser, DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = execution.TenantId;
        var rows = await (
            from account in db.Accounts.IgnoreQueryFilters().AsNoTracking()
            join customer in db.Users.IgnoreQueryFilters().AsNoTracking() on account.CustomerId equals customer.Id
            where account.TenantId == tenantId
                  && customer.TenantId == tenantId
                  && customer.Role == UserRole.Customer
                  && customer.AdviserId == adviser.Id
            select new { Account = account, CustomerId = customer.PublicId, CustomerName = customer.Name }
        ).ToListAsync(cancellationToken);

        var custodial = rows.Where(row => row.Account.CustodyMode == CustodyMode.Custodial).Select(row => row.Account).ToList();
        var custodialIds = custodial.Select(account => account.Id).ToList();
        var stored = custodialIds.Count == 0
            ? []
            : await db.StoredValuations.IgnoreQueryFilters().AsNoTracking().Include(row => row.Lines)
                .Where(row => row.TenantId == tenantId && row.Date == date && custodialIds.Contains(row.AccountId))
                .ToListAsync(cancellationToken);
        var storedByAccount = stored.ToDictionary(row => row.AccountId);
        var closures = await ValuationDays.LastClosuresAsync(
            db,
            tenantId,
            custodial.Where(account => account.Status == AccountStatus.Closed).Select(account => account.Id).ToList(),
            cancellationToken);
        var instrumentIds = stored.SelectMany(row => row.Lines).Select(line => line.InstrumentId).Distinct().ToList();
        var instruments = instrumentIds.Count == 0
            ? []
            : await db.Instruments.IgnoreQueryFilters().AsNoTracking()
                .Where(instrument => instrumentIds.Contains(instrument.Id))
                .ToDictionaryAsync(
                    instrument => instrument.Id,
                    instrument => new ValuedInstrument(
                        instrument.Id, instrument.PublicId, instrument.Symbol, instrument.Name, instrument.QuoteCurrency.Trim()),
                    cancellationToken);

        var sources = new List<AdviserPastAccount>();
        foreach (var row in rows)
        {
            var account = row.Account;
            var source = new AdviserPastAccount(
                row.CustomerId, row.CustomerName, account.PublicId, account.Name, account.Type.ToString(),
                account.CustodyMode.ToString(), account.Currency.Trim(), false, 0m, []);
            if (account.CustodyMode == CustodyMode.Custodial)
            {
                if (storedByAccount.TryGetValue(account.Id, out var valuation))
                {
                    sources.Add(source with
                    {
                        Cash = valuation.Cash,
                        Holdings = valuation.Lines.Select(line => new AdviserPastHolding(
                            instruments[line.InstrumentId].PublicId,
                            instruments[line.InstrumentId].Symbol,
                            line.Quantity,
                            line.LedgerQuantity,
                            line.Status,
                            line.PriceAmount,
                            line.PriceDate,
                            line.MarketValue,
                            instruments[line.InstrumentId].QuoteCurrency)).ToList()
                    });
                }
                else if (ValuationDays.IsExpected(
                             date,
                             ValuationDays.FirstDay(account.Created),
                             ValuationDays.LastDay(account.Status, closures.GetValueOrDefault(account.Id))))
                {
                    sources.Add(source with { NotValuedYet = true });
                }

                continue;
            }

            var read = await valueAccount.ForDayAsync(account, date, cancellationToken);
            sources.Add(source with
            {
                Cash = read.Valuation.Cash,
                Holdings = read.Valuation.Lines.Select(line => new AdviserPastHolding(
                    read.Instruments[line.InstrumentId].PublicId,
                    read.Instruments[line.InstrumentId].Symbol,
                    line.Quantity,
                    line.LedgerQuantity,
                    line.Status,
                    line.Price?.Amount,
                    line.Price?.AsOfDate,
                    line.MarketValue,
                    read.Instruments[line.InstrumentId].QuoteCurrency)).ToList()
            });
        }

        var selection = AdviserBalancesPastRows.Select(sources);
        return AdviserBalancesPdf.RenderPast(new AdviserBalancesPastPdfModel(
            tenant.Name,
            adviser.Name,
            date,
            execution.AsOf!.Value,
            time.GetUtcNow(),
            selection.Lines.Select(ToPastPdfRow).ToList(),
            selection.Subtotals.Select(ToPdfSubtotal).ToList(),
            selection.ModeSubtotals
                .Select(block => new AdviserBalancesPdfSubtotalBlock(block.CustodyMode, block.Subtotals.Select(ToPdfSubtotal).ToList()))
                .ToList(),
            selection.HoldingsNotValued,
            selection.AccountsNotValuedYet));
    }

    private AdviserBalancesPdfRow ToPastPdfRow(AdviserBalanceLine line) => line switch
    {
        AdviserCashLine cash => new AdviserBalancesPdfCashRow(
            cash.CustomerName, cash.AccountName, cash.AccountType, cash.CustodyMode, FormatMoney(cash.Balance, cash.Currency)),
        AdviserPastHoldingLine holding => new AdviserBalancesPdfPastHoldingRow(
            holding.CustomerName,
            holding.AccountName,
            holding.CustodyMode,
            holding.Symbol,
            holding.Quantity == holding.LedgerQuantity
                ? FormatQuantity(holding.Quantity)
                : $"{FormatQuantity(holding.Quantity)} (ledger {FormatQuantity(holding.LedgerQuantity)})",
            holding.PriceAmount is { } price && holding.PriceDate is { } priceDate
                ? $"{FormatPrice(price, holding.MarketCurrency)} ({priceDate:yyyy-MM-dd})"
                : string.Empty,
            holding.MarketValue is { } value ? FormatMoney(value, holding.MarketCurrency) : string.Empty,
            AdviserBalancesPastRows.StatusLabel(holding.Status) ?? string.Empty),
        AdviserNotValuedLine notValued => new AdviserBalancesPdfNotValuedRow(
            notValued.CustomerName, notValued.AccountName, notValued.AccountType, notValued.CustodyMode),
        _ => throw new InvalidOperationException($"Unsupported balance line {line.GetType().Name}.")
    };

    private static string FormatQuantity(decimal quantity) => quantity.ToString("0.################", CultureInfo.InvariantCulture);

    /// <summary>A price with at least the decimal places of its currency, and every place it was stored with.</summary>
    private string FormatPrice(decimal amount, string currency)
    {
        var places = currencies.TryGet(currency.Trim())?.DecimalPlaces ?? 2;
        var normalized = amount / 1.000000000000000000000000000000000m;
        var scale = (decimal.GetBits(normalized)[3] >> 16) & 0xFF;
        return amount.ToString("F" + Math.Max(places, scale), CultureInfo.InvariantCulture);
    }

    private AdviserBalancesPdfSubtotal ToPdfSubtotal(AdviserBalanceSubtotal subtotal) => new(
        subtotal.Currency,
        FormatMoney(subtotal.Cash, subtotal.Currency),
        FormatMoney(subtotal.MarketValue, subtotal.Currency));

    private AdviserBalanceHolding ToHolding(HoldingRow holding)
    {
        var price = marketData.TryGetPrice(holding.InstrumentInternalId, null);
        return new AdviserBalanceHolding(
            holding.InstrumentId,
            holding.Symbol,
            holding.Quantity,
            holding.CostAmount,
            holding.CostCurrency,
            price?.Amount,
            price?.Currency,
            holding.QuoteCurrency);
    }

    private AdviserBalancesPdfRow ToPdfRow(AdviserBalanceLine line) => line switch
    {
        AdviserCashLine cash => new AdviserBalancesPdfCashRow(
            cash.CustomerName,
            cash.AccountName,
            cash.AccountType,
            cash.CustodyMode,
            FormatMoney(cash.Balance, cash.Currency)),
        AdviserHoldingLine holding => new AdviserBalancesPdfHoldingRow(
            holding.CustomerName,
            holding.AccountName,
            holding.CustodyMode,
            holding.Symbol,
            holding.Quantity.ToString("0.################", CultureInfo.InvariantCulture),
            FormatMoney(holding.CostAmount, holding.CostCurrency),
            FormatMoney(holding.MarketValue, holding.MarketCurrency)),
        _ => throw new InvalidOperationException($"Unsupported balance line {line.GetType().Name}.")
    };

    private string FormatMoney(decimal amount, string currency)
    {
        var code = currency.Trim();
        var catalog = currencies.TryGet(code)
                      ?? throw new InvalidOperationException($"Currency {code} is not in the catalog.");
        return CashAmountText.Format(amount, catalog.Code, catalog.DecimalPlaces);
    }

    private sealed record AccountRow(
        int Id,
        Guid AccountId,
        string AccountName,
        AccountType Type,
        CustodyMode CustodyMode,
        AccountStatus Status,
        string Currency,
        Guid CustomerId,
        string CustomerName);

    private sealed record HoldingRow(
        int AccountId,
        Guid InstrumentId,
        int InstrumentInternalId,
        string Symbol,
        decimal Quantity,
        decimal CostAmount,
        string CostCurrency,
        string QuoteCurrency);
}

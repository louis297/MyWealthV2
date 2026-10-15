using System.Globalization;
using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public sealed class AdviserBalancesReportGenerator(
    IApplicationDbContext db,
    ICurrencyCatalog currencies,
    IMarketData marketData,
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
        var binding = AdviserBalancesBinder.Bind(parameters.RootElement, UserRole.TenantAdmin, callerPublicId: null);
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
                join transaction in db.Transactions.AsNoTracking() on leg.TransactionId equals transaction.Id
                where accountIds.Contains(transaction.AccountId)
                group leg by transaction.AccountId
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
            account.Currency.Trim(),
            cashByAccount.GetValueOrDefault(account.Id),
            account.Status == AccountStatus.Closed,
            holdingsByAccount.TryGetValue(account.Id, out var holdings)
                ? holdings.Select(ToHolding).ToList()
                : [])).ToList();

        var selection = AdviserBalancesRows.Select(sources, binding.IncludeClosedAccounts);
        var rows = selection.Lines.Select(ToPdfRow).ToList();
        var subtotals = selection.Subtotals
            .Select(subtotal => new AdviserBalancesPdfSubtotal(
                subtotal.Currency,
                FormatMoney(subtotal.Cash, subtotal.Currency),
                FormatMoney(subtotal.MarketValue, subtotal.Currency)))
            .ToList();

        return AdviserBalancesPdf.Render(new AdviserBalancesPdfModel(
            tenant.Name,
            adviser.Name,
            execution.AsOf.Value,
            time.GetUtcNow(),
            binding.IncludeClosedAccounts,
            rows,
            subtotals));
    }

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
            FormatMoney(cash.Balance, cash.Currency)),
        AdviserHoldingLine holding => new AdviserBalancesPdfHoldingRow(
            holding.CustomerName,
            holding.AccountName,
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

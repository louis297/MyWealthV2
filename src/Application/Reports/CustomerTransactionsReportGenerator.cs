using System.Globalization;
using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public sealed class CustomerTransactionsReportGenerator(
    IApplicationDbContext db,
    ICurrencyCatalog currencies,
    TimeProvider time) : IReportGenerator
{
    public string Code => CustomerTransactionsBinder.Code;

    public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        using var parameters = JsonDocument.Parse(execution.ParametersJson);
        var binding = CustomerTransactionsBinder.Bind(parameters.RootElement);
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.Id == execution.TenantId, cancellationToken)
                     ?? throw new InvalidOperationException("Tenant was not found.");
        var customer = await db.Users.AsNoTracking()
                           .SingleOrDefaultAsync(
                               row => row.PublicId == binding.CustomerId
                                      && row.TenantId == execution.TenantId
                                      && row.Role == UserRole.Customer,
                               cancellationToken)
                       ?? throw new InvalidOperationException("Customer is not in scope.");

        var accounts = await db.Accounts.AsNoTracking()
            .Where(row => row.TenantId == execution.TenantId && row.CustomerId == customer.Id)
            .Select(row => new AccountRow(row.Id, row.Name, row.Status))
            .ToListAsync(cancellationToken);
        var accountIds = accounts.Select(row => row.Id).ToList();
        var transactions = accountIds.Count == 0
            ? []
            : await db.Transactions.AsNoTracking()
                .Where(row => accountIds.Contains(row.AccountId))
                .Include(row => row.CashLeg)
                .Include(row => row.SecurityLegs)
                .ToListAsync(cancellationToken);

        var instrumentIds = transactions
            .SelectMany(row => row.SecurityLegs)
            .Select(leg => leg.InstrumentId)
            .Distinct()
            .ToList();
        var symbols = instrumentIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Instruments.AsNoTracking()
                .Where(row => instrumentIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Symbol, cancellationToken);

        var accountsById = accounts.ToDictionary(row => row.Id);
        var sources = new List<CustomerTransactionSource>(transactions.Count);
        var cashById = new Dictionary<int, string>();
        var instrumentById = new Dictionary<int, string>();
        var quantityById = new Dictionary<int, string>();
        foreach (var transaction in transactions)
        {
            var account = accountsById[transaction.AccountId];
            sources.Add(new CustomerTransactionSource(
                transaction.Id,
                transaction.OriginalTransactionId,
                transaction.BookedAt,
                account.Status == AccountStatus.Closed,
                account.Name,
                transaction.Type.ToString(),
                transaction.Memo));
            cashById[transaction.Id] = FormatCash(transaction.CashLeg);
            var legs = transaction.SecurityLegs.OrderBy(leg => leg.Id).ToList();
            instrumentById[transaction.Id] = string.Join(", ", legs.Select(leg => Symbol(symbols, leg.InstrumentId)));
            quantityById[transaction.Id] = string.Join(
                ", ",
                legs.Select(leg => leg.Quantity.ToString("0.################", CultureInfo.InvariantCulture)));
        }

        var selected = CustomerTransactionsRows.Select(
            sources,
            binding.From,
            binding.To,
            binding.IncludeClosedAccounts);
        var rows = selected
            .Select(row => new CustomerTransactionsPdfRow(
                row.BookedAt,
                row.AccountName,
                row.Type,
                cashById[row.Id],
                instrumentById[row.Id],
                quantityById[row.Id],
                row.Memo))
            .ToList();

        return CustomerTransactionsPdf.Render(new CustomerTransactionsPdfModel(
            tenant.Name,
            time.GetUtcNow(),
            customer.Name,
            binding.From,
            binding.To,
            binding.IncludeClosedAccounts,
            rows));
    }

    private string FormatCash(TransactionCashLeg? leg)
    {
        if (leg is null)
        {
            return string.Empty;
        }

        var currency = currencies.TryGet(leg.Currency)
                       ?? throw new InvalidOperationException($"Currency {leg.Currency} is not in the catalog.");
        return CashAmountText.Format(leg.Amount, leg.Currency, currency.DecimalPlaces);
    }

    private static string Symbol(IReadOnlyDictionary<int, string> symbols, int instrumentId)
    {
        if (!symbols.TryGetValue(instrumentId, out var symbol))
        {
            throw new InvalidOperationException($"Instrument {instrumentId} was not found.");
        }

        return symbol;
    }

    private sealed record AccountRow(int Id, string Name, AccountStatus Status);
}

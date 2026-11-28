using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The Tax Year Income Summary (docs/features/income-summary.md R20 to R22): the names and the tax residency on file,
/// one read of the Customer's tax year, then the content and the PDF. It reads no price, no rate and no Transaction of
/// its own, and writes nothing but the result file the job stores. An incomplete summary still gives a file.
/// </summary>
public sealed class CustomerIncomeSummaryReportGenerator(
    IApplicationDbContext db,
    ICustomerTaxLedger ledger,
    ICurrencyCatalog currencies,
    TimeProvider time) : IReportGenerator
{
    public string Code => CustomerIncomeSummaryBinder.Code;

    public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        var generatedAt = time.GetUtcNow();
        using var parameters = JsonDocument.Parse(execution.ParametersJson);
        var binding = CustomerIncomeSummaryBinder.Bind(parameters.RootElement, LedgerDay.Of(generatedAt));
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

        // R19: the residency on file is printed; it refuses nothing.
        var residency = await CustomerTaxYearParameters.ResidencyAsync(db, execution.TenantId, customer.Id, cancellationToken);
        var accountNames = await db.Accounts.AsNoTracking()
            .Where(row => row.TenantId == execution.TenantId && row.CustomerId == customer.Id)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        // R20: the platform time just before the one read is the instant the ledger was read.
        var ledgerReadAt = time.GetUtcNow();
        var read = await ledger.ReadAsync(execution.TenantId, customer.Id, binding.TaxYear, cancellationToken);

        var names = new CustomerIncomeSummaryNames(
            tenant.Name,
            customer.Name,
            accountNames,
            currencies.List().ToDictionary(currency => currency.Code, currency => currency.DecimalPlaces, StringComparer.Ordinal));
        return CustomerIncomeSummaryPdf.Render(CustomerIncomeSummaryReport.Build(read, names, residency, generatedAt, ledgerReadAt));
    }
}

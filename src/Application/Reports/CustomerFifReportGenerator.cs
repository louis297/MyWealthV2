using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The FIF report (docs/features/fif-report.md R8 to R11): the tax residency again, one read of the Customer's tax
/// year, then the content and the PDF. It reads no price, no rate and no Transaction of its own, and writes nothing
/// but the result file the job stores.
/// </summary>
public sealed class CustomerFifReportGenerator(
    IApplicationDbContext db,
    ICustomerTaxLedger ledger,
    ICurrencyCatalog currencies,
    TimeProvider time) : IReportGenerator
{
    public string Code => CustomerFifBinder.Code;

    public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        var generatedAt = time.GetUtcNow();
        using var parameters = JsonDocument.Parse(execution.ParametersJson);
        var binding = CustomerFifBinder.Bind(parameters.RootElement, LedgerDay.Of(generatedAt));
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

        // R8: the residency is read again; a change since the request fails the execution and writes no file.
        var residency = await CustomerFifBinder.ResidencyAsync(db, execution.TenantId, customer.Id, cancellationToken);
        if (residency != TaxResidency.NewZealand)
            throw new InvalidOperationException(CustomerFifBinder.OverseasMessage);

        var accountNames = await db.Accounts.AsNoTracking()
            .Where(row => row.TenantId == execution.TenantId && row.CustomerId == customer.Id)
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);

        // R9: the platform time just before the one read is the instant the ledger was read.
        var ledgerReadAt = time.GetUtcNow();
        var read = await ledger.ReadAsync(execution.TenantId, customer.Id, binding.TaxYear, cancellationToken);

        var names = new CustomerFifNames(
            tenant.Name,
            customer.Name,
            accountNames,
            currencies.List().ToDictionary(currency => currency.Code, currency => currency.DecimalPlaces, StringComparer.Ordinal));
        return CustomerFifPdf.Render(CustomerFifReport.Build(read, names, residency, generatedAt, ledgerReadAt));
    }
}

using System.Text.Json;
using MyWealthV2.Application.BankReconciliations;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// Bank-reconciliation R24, R25: renders a stored run, the one named or the newest of the account at generation
/// time. It never starts a run.
/// </summary>
public sealed class BankReconciliationReportGenerator(
    IApplicationDbContext db,
    ICurrencyCatalog currencies,
    TimeProvider time) : IReportGenerator
{
    public const string NoRunMessage = "No bank reconciliation has been run for this account.";

    public string Code => BankReconciliationBinder.Code;

    public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        using var parameters = JsonDocument.Parse(execution.ParametersJson);
        var binding = BankReconciliationBinder.Bind(parameters.RootElement);
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.Id == execution.TenantId, cancellationToken)
                     ?? throw new InvalidOperationException("Tenant was not found.");
        var account = await BankReconciliationBinder.EnsureScopeAsync(db, execution.TenantId, binding, cancellationToken);

        var runs = db.BankReconciliations.AsNoTracking()
            .Where(row => row.TenantId == execution.TenantId && row.ClientMoneyAccountId == account.Id);
        if (binding.ReconciliationId is Guid runId)
        {
            runs = runs.Where(row => row.PublicId == runId);
        }

        var run = await runs.OrderByDescending(row => row.RanAt).ThenByDescending(row => row.Id)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new InvalidOperationException(NoRunMessage);
        var dto = await BankReconciliationReader.ToDtoAsync(db, run, cancellationToken);
        var places = currencies.TryGet(dto.ClientMoneyAccount.Currency)?.DecimalPlaces ?? 2;

        return BankReconciliationPdf.Render(new BankReconciliationPdfModel(
            tenant.Name, time.GetUtcNow(), dto, run.Status == BankReconciliationStatus.NoBankBalance, places));
    }
}

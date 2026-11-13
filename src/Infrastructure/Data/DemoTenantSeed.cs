using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Data;

/// <summary>
/// The demo seed (demo-tenant-seed R4–R12): creates the demo tenant, its demo catalog and its
/// report grants when missing. Never changes an existing row. One transaction per run.
/// The caller reloads the currency catalog first.
/// </summary>
public static class DemoTenantSeed
{
    public const string CreatedBy = "system";

    public const string Argument = "--seed-demo";

    public static bool IsRequested(string[] args) => false;

    private const string ReportingCurrency = "USD";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var currencies = services.GetRequiredService<ICurrencyCatalog>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoTenantSeed));

        Summary summary = default;
        await db.ExecuteInTransactionAsync(async token =>
        {
            db.ClearTrackedChanges();
            summary = await SeedAsync(db, currencies, logger, token);
        }, cancellationToken);

        logger.LogInformation(
            "Demo seed: tenant {TenantCode} {TenantOutcome}; instruments {InstrumentsCreated} created, {InstrumentsExisting} existing; reports {ReportsGranted} granted, {ReportsExisting} existing",
            DemoCatalog.TenantCode,
            summary.TenantCreated ? "created" : "existing",
            summary.InstrumentsCreated,
            summary.InstrumentsExisting,
            summary.ReportsGranted,
            summary.ReportsExisting);
    }

    private static async Task<Summary> SeedAsync(
        ApplicationDbContext db,
        ICurrencyCatalog currencies,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DemoCatalog.TenantCode, cancellationToken);
        var tenantCreated = tenant is null;
        if (tenant is null)
        {
            tenant = Tenant.Create(DemoCatalog.TenantName, DemoCatalog.TenantCode, Require(currencies, ReportingCurrency));
            tenant.CreatedBy = CreatedBy;
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!tenant.IsActive)
        {
            logger.LogWarning("Demo seed: tenant {TenantCode} exists and is disabled.", DemoCatalog.TenantCode);
        }

        var existingSymbols = await db.Instruments
            .Where(row => row.TenantId == tenant.Id)
            .Select(row => row.Symbol)
            .ToListAsync(cancellationToken);
        var known = existingSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var instrumentsCreated = 0;
        foreach (var item in DemoCatalog.Instruments)
        {
            if (known.Contains(item.Symbol))
            {
                continue;
            }

            var instrument = Instrument.Create(tenant.Id, item.Symbol, item.Name, Require(currencies, item.QuoteCurrency));
            instrument.CreatedBy = CreatedBy;
            db.Instruments.Add(instrument);
            instrumentsCreated++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var reports = await db.Reports
            .Where(row => DemoCatalog.ReportCodes.Contains(row.Code))
            .ToListAsync(cancellationToken);
        var granted = await db.TenantReports
            .Where(row => row.TenantId == tenant.Id)
            .Select(row => row.ReportId)
            .ToListAsync(cancellationToken);
        var reportsGranted = 0;
        var reportsExisting = 0;
        foreach (var code in DemoCatalog.ReportCodes)
        {
            var report = reports.SingleOrDefault(row => row.Code == code)
                         ?? throw new InvalidOperationException(
                             $"Demo seed: report '{code}' is not in the report catalog.");
            if (granted.Contains(report.Id))
            {
                reportsExisting++;
                continue;
            }

            if (!report.IsActive)
            {
                logger.LogWarning("Demo seed: report {ReportCode} is inactive and is not granted.", code);
                continue;
            }

            db.TenantReports.Add(TenantReport.Grant(tenant.Id, report.Id));
            reportsGranted++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new Summary(
            tenantCreated,
            instrumentsCreated,
            DemoCatalog.Instruments.Count - instrumentsCreated,
            reportsGranted,
            reportsExisting);
    }

    private static Currency Require(ICurrencyCatalog currencies, string code) =>
        currencies.TryGet(code)
        ?? throw new InvalidOperationException($"Demo seed: currency '{code}' is not in the currency catalog.");

    private readonly record struct Summary(
        bool TenantCreated,
        int InstrumentsCreated,
        int InstrumentsExisting,
        int ReportsGranted,
        int ReportsExisting);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Instruments;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Data;

/// <summary>
/// The demo seed (demo-tenant-seed R4–R12): creates the demo tenant, its demo catalog and its
/// report grants when missing. Never changes an existing row, except that it classifies a demo catalog instrument
/// with no tax attribute (instrument-tax-attributes R24). One transaction per run.
/// The caller reloads the currency catalog first.
/// </summary>
public static class DemoTenantSeed
{
    public const string CreatedBy = "system";

    public const string Argument = "--seed-demo";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Argument, StringComparison.Ordinal);

    private const string ReportingCurrency = "USD";

    /// <summary>Orders §9.1: the broker connection a seeded tenant gets when it is configured on the host.</summary>
    public const string BrokerConnection = "simulator";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var currencies = services.GetRequiredService<ICurrencyCatalog>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoTenantSeed));
        var withBroker = services.GetService<IBrokerConnections>()?.IsConfigured(BrokerConnection) == true;
        var latestTaxYear = InstrumentTaxYears.Latest(services.GetService<TimeProvider>() ?? TimeProvider.System);

        Summary summary = default;
        await db.ExecuteInTransactionAsync(async token =>
        {
            db.ClearTrackedChanges();
            summary = await SeedAsync(db, currencies, withBroker, latestTaxYear, logger, token);
        }, cancellationToken);

        logger.LogInformation(
            "Demo seed: tenant {TenantCode} {TenantOutcome}; instruments {InstrumentsCreated} created, {InstrumentsExisting} existing; instruments classified {InstrumentsClassified}, already classified {InstrumentsAlreadyClassified}; reports {ReportsGranted} granted, {ReportsExisting} existing",
            DemoCatalog.TenantCode,
            summary.TenantCreated ? "created" : "existing",
            summary.InstrumentsCreated,
            summary.InstrumentsExisting,
            summary.InstrumentsClassified,
            summary.InstrumentsAlreadyClassified,
            summary.ReportsGranted,
            summary.ReportsExisting);
    }

    private static async Task<Summary> SeedAsync(
        ApplicationDbContext db,
        ICurrencyCatalog currencies,
        bool withBroker,
        int latestTaxYear,
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
            // Orders §9.1: only the tenant this run creates; an existing tenant is left alone.
            if (withBroker)
            {
                tenant.SetBrokerConnection(BrokerConnection);
            }

            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!tenant.IsActive)
        {
            logger.LogWarning("Demo seed: tenant {TenantCode} exists and is disabled.", DemoCatalog.TenantCode);
        }

        var existing = (await db.Instruments
                .Include(row => row.FifTreatments)
                .Where(row => row.TenantId == tenant.Id)
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Symbol, StringComparer.OrdinalIgnoreCase);
        var instrumentsCreated = 0;
        var instrumentsClassified = 0;
        foreach (var item in DemoCatalog.Instruments)
        {
            if (!existing.TryGetValue(item.Symbol, out var instrument))
            {
                instrument = Instrument.Create(tenant.Id, item.Symbol, item.Name, Require(currencies, item.QuoteCurrency));
                instrument.CreatedBy = CreatedBy;
                db.Instruments.Add(instrument);
                instrumentsCreated++;
            }

            // Instrument-tax-attributes R24: a new instrument, or an existing one with no tax attribute at all.
            if (DemoCatalog.ClassifyIfUnclassified(
                    instrument, item.IssuerCountry, item.Treatment, item.Note, latestTaxYear, CreatedBy))
            {
                instrumentsClassified++;
            }
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
            instrumentsClassified,
            DemoCatalog.Instruments.Count - instrumentsClassified,
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
        int InstrumentsClassified,
        int InstrumentsAlreadyClassified,
        int ReportsGranted,
        int ReportsExisting);
}

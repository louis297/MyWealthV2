using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Application.Interest.RateSchedules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Common;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Infrastructure.Data;

public static class TestSeed
{
    public const string CreatedBy = "system";

    public const string DemoCustomerEmail = "customer@localhost";

    public const string DemoCustomerName = "Demo Customer";

    public const string SecondCustomerEmail = "customer2@localhost";

    public const string SecondCustomerName = "Demo Customer 2";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await EnsureFxRatesAsync(db, cancellationToken);
        foreach (var firm in Firms)
        {
            await SeedFirmAsync(services, db, firm, cancellationToken);
        }
    }

    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        await EnsureFxRatesAsync(db, cancellationToken);
        foreach (var firm in Firms)
        {
            await SeedFirmAsync(services: null, db, firm, cancellationToken);
        }
    }

    private static readonly (string BaseCurrency, string QuoteCurrency, decimal Rate)[] SeedRates =
    [
        ("USD", "NZD", 1.50000000m),
        ("USD", "AUD", 1.40000000m),
        ("USD", "EUR", 0.90000000m),
        ("USD", "GBP", 0.80000000m),
        ("USD", "JPY", 150.00000000m),
        ("NZD", "USD", 0.66666667m),
        ("AUD", "USD", 0.71428571m),
        ("EUR", "USD", 1.11111111m),
        ("GBP", "USD", 1.25000000m),
        ("JPY", "USD", 0.00666667m)
    ];

    private static async Task EnsureFxRatesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var asOf = new DateOnly(2026, 1, 1);
        var existing = await db.FxRates
            .Where(row => row.AsOfDate == asOf)
            .Select(row => new { row.BaseCurrency, row.QuoteCurrency })
            .ToListAsync(cancellationToken);
        var known = existing
            .Select(row => $"{row.BaseCurrency.Trim()}|{row.QuoteCurrency.Trim()}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pair in SeedRates)
        {
            if (!known.Add($"{pair.BaseCurrency}|{pair.QuoteCurrency}"))
            {
                continue;
            }

            db.FxRates.Add(new FxRate
            {
                BaseCurrency = pair.BaseCurrency,
                QuoteCurrency = pair.QuoteCurrency,
                Rate = pair.Rate,
                AsOfDate = asOf,
                Source = "Frankfurter",
                FetchedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private readonly record struct SeedFirm(
        string TenantCode,
        string TenantAdminEmail,
        string AdviserEmail,
        string CustomerEmail,
        string CustomerName);

    private static readonly SeedFirm[] Firms =
    [
        new(
            DevelopmentIdentitySeeder.DemoTenantCode,
            DevelopmentIdentitySeeder.TenantAdminEmail,
            DevelopmentIdentitySeeder.AdviserEmail,
            DemoCustomerEmail,
            DemoCustomerName),
        new(
            DevelopmentIdentitySeeder.SecondTenantCode,
            DevelopmentIdentitySeeder.SecondTenantAdminEmail,
            DevelopmentIdentitySeeder.SecondAdviserEmail,
            SecondCustomerEmail,
            SecondCustomerName),
    ];

    private readonly record struct SeedInstrument(
        string Symbol,
        string Name,
        string QuoteCurrency,
        bool Active,
        decimal? Price);

    // Development-only fake prices for the shared demo catalog (demo-tenant-seed R13).
    private static readonly Dictionary<string, decimal> DevelopmentPrices = new(StringComparer.Ordinal)
    {
        ["VTI"] = 250m,
        ["AAPL"] = 180m,
        ["MSFT"] = 400m,
        ["AIA"] = 8.50m,
        ["FPH"] = 30m,
        ["MEL"] = 5.50m,
        ["BHP"] = 45m,
        ["CBA"] = 130m,
        ["WOW"] = 35m,
        ["SAP"] = 180m,
        ["AIR"] = 150m,
        ["OR"] = 400m,
        ["SHEL"] = 28m,
        ["HSBA"] = 7m,
        ["ULVR"] = 45m,
        ["7203"] = 3000m,
        ["6758"] = 13000m,
        ["9984"] = 8000m
    };

    private const string SimulatedSuffix = ".SIM";
    private const string SimulatorSource = "Simulator";

    private static readonly SeedInstrument[] Catalog =
    [
        .. DemoCatalog.Instruments.Select(item =>
            new SeedInstrument(item.Symbol, item.Name, item.QuoteCurrency, true, DevelopmentPrices[item.Symbol])),
        new("NZD-CASH", "Demo disabled cash proxy", "NZD", false, null),

        // Simulated instruments §9.1: the simulator's standing instruments, so a host without the simulator still
        // has a price. A market data connection with suffix .SIM prices them from then on.
        new("ACME.SIM", "Acme Simulated Holdings", "USD", true, 100m),
        new("KIWI.SIM", "Kiwi Simulated Fund", "NZD", true, 5m)
    ];

    private static async Task SeedFirmAsync(
        IServiceProvider? services,
        ApplicationDbContext db,
        SeedFirm firm,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == firm.TenantCode, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        await EnsureReportGrantsAsync(db, tenant.Id, cancellationToken);
        await EnsureCatalogAsync(db, tenant.Id, cancellationToken);
        await EnsureClientMoneyAccountsAsync(db, tenant, cancellationToken);
        await EnsureSafekeepingAccountsAsync(db, tenant, cancellationToken);

        if (services is not null)
        {
            await EnsureCustomerAsync(services, db, tenant, firm, cancellationToken);
        }

        await EnsureAccountsAsync(db, tenant.Id, firm.CustomerEmail, cancellationToken);
        await EnsureTradeFeeScheduleAsync(db, tenant.Id, cancellationToken);
        await EnsureNominatedBankAccountAsync(db, tenant, firm, cancellationToken);
        if (firm.TenantCode == DevelopmentIdentitySeeder.DemoTenantCode)
        {
            await EnsureInterestRateScheduleAsync(db, tenant.Id, "USD", "United States Dollar", [new FeeBand(0m, 3m), new FeeBand(100000m, 2m)], cancellationToken);
            await EnsureInterestRateScheduleAsync(db, tenant.Id, "NZD", "New Zealand Dollar", [new FeeBand(0m, 3.5m)], cancellationToken);
            await EnsureTaxProfileAsync(db, tenant, firm, cancellationToken);
        }
    }

    /// <summary>
    /// Interest run §9.1: one interest rate schedule per currency in the demo tenant, from the day after the cut-off
    /// date at seed time, so it obeys interest-run R13. Idempotent per (tenant, currency).
    /// </summary>
    private static async Task EnsureInterestRateScheduleAsync(
        ApplicationDbContext db,
        int tenantId,
        string currency,
        string currencyName,
        FeeBand[] bands,
        CancellationToken cancellationToken)
    {
        if (await db.InterestRateSchedules.AnyAsync(row => row.TenantId == tenantId && row.Currency == currency, cancellationToken))
        {
            return;
        }

        var cutOff = await InterestCutOff.ForAsync(db, tenantId, currency, InterestCutOff.Today(TimeProvider.System), cancellationToken);
        var schedule = InterestRateSchedule.Create(tenantId, Currency.Create(currency, currencyName, 2), cutOff.AddDays(1), bands, cutOff);
        schedule.Created = TimeProvider.System.GetUtcNow();
        schedule.CreatedBy = CreatedBy;
        db.InterestRateSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Interest run §9.1: the demo Customer's tax profile, IRD number held and 17.5 % chosen, saved by the demo
    /// TenantAdmin. The other seeded Customers stay not provided. Skipped while either person is missing.</summary>
    private static async Task EnsureTaxProfileAsync(
        ApplicationDbContext db,
        Tenant tenant,
        SeedFirm firm,
        CancellationToken cancellationToken)
    {
        var people = await db.DomainUsers
            .Where(row => row.TenantId == tenant.Id && (row.Email == firm.TenantAdminEmail || row.Email == firm.CustomerEmail))
            .ToListAsync(cancellationToken);
        var admin = people.SingleOrDefault(row => row.Email == firm.TenantAdminEmail);
        var customer = people.SingleOrDefault(row => row.Email == firm.CustomerEmail);
        if (admin is null || customer is null
            || await db.CustomerTaxProfiles.AnyAsync(row => row.UserId == customer.Id, cancellationToken))
        {
            return;
        }

        var profile = CustomerTaxProfile.Create(
            tenant.Id, customer.Id, TaxResidency.NewZealand, true, 17.5m, admin.Id, TimeProvider.System.GetUtcNow());
        profile.Created = TimeProvider.System.GetUtcNow();
        profile.CreatedBy = CreatedBy;
        db.CustomerTaxProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The account number of the seeded nominated bank account (withdrawals §9.1).</summary>
    public const string SeededPayeeAccountNumber = "SIM-PAYEE-0001";

    /// <summary>
    /// One Verified USD nominated bank account for the firm's Customer (withdrawals §9.1), added by the firm's Adviser
    /// and verified by its TenantAdmin, so a withdrawal can be requested in Development without a check first.
    /// Idempotent on (Customer, account number); skipped while any of the three people is missing.
    /// </summary>
    private static async Task EnsureNominatedBankAccountAsync(
        ApplicationDbContext db,
        Tenant tenant,
        SeedFirm firm,
        CancellationToken cancellationToken)
    {
        var people = await db.DomainUsers
            .Where(row => row.TenantId == tenant.Id
                          && (row.Email == firm.TenantAdminEmail || row.Email == firm.AdviserEmail || row.Email == firm.CustomerEmail))
            .ToListAsync(cancellationToken);
        var admin = people.SingleOrDefault(row => row.Email == firm.TenantAdminEmail);
        var adviser = people.SingleOrDefault(row => row.Email == firm.AdviserEmail);
        var customer = people.SingleOrDefault(row => row.Email == firm.CustomerEmail);
        if (admin is null || adviser is null || customer is null)
        {
            return;
        }

        var exists = await db.NominatedBankAccounts.AnyAsync(
            row => row.CustomerId == customer.Id && row.AccountNumber == SeededPayeeAccountNumber, cancellationToken);
        if (exists)
        {
            return;
        }

        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD", cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var row = NominatedBankAccount.Add(
            tenant.Id, customer.Id, usd, "Simulator Bank", customer.Name, SeededPayeeAccountNumber, adviser.Id, now);
        row.Verify(admin.Id, "Seeded for Development: no evidence was checked.", now);
        row.Created = now;
        row.CreatedBy = CreatedBy;
        db.NominatedBankAccounts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static readonly string[] ClientMoneyCurrencies = ["USD", "NZD"];

    /// <summary>The bank connection the seeded client money accounts are read through (deposit-matching §9.1).</summary>
    public const string SimulatorBankConnection = "simulator";

    /// <summary>In-kind transfers §9.1: the custodian connection of the seeded safekeeping accounts.</summary>
    public const string SimulatorCustodianConnection = "simulator";

    /// <summary>
    /// An active client money account in USD and in NZD (client-money-accounts §9.1), so the Custodial demo
    /// account has deposit instructions and new Custodial accounts open in those currencies. Idempotent on
    /// (tenant, currency, active).
    /// </summary>
    private static async Task EnsureClientMoneyAccountsAsync(
        ApplicationDbContext db,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        foreach (var code in ClientMoneyCurrencies)
        {
            var exists = await db.ClientMoneyAccounts.AnyAsync(
                row => row.TenantId == tenant.Id && row.Currency == code && row.IsActive, cancellationToken);
            if (exists)
            {
                continue;
            }

            var currency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == code, cancellationToken);
            var row = ClientMoneyAccount.Create(
                tenant.Id,
                currency,
                "Simulator Bank",
                $"{tenant.Name} Client Funds Trust Account",
                $"SIM-{tenant.Code.ToUpperInvariant()}-{code}",
                SimulatorBankConnection);
            row.Created = TimeProvider.System.GetUtcNow();
            row.CreatedBy = CreatedBy;
            db.ClientMoneyAccounts.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// In-kind transfers §9.1: for each client money currency an active safekeeping account on connection
    /// <c>simulator</c>, the standing custodian accounts the AppHost opens in the simulator. Idempotent on
    /// (tenant, currency, active).
    /// </summary>
    private static async Task EnsureSafekeepingAccountsAsync(
        ApplicationDbContext db,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        foreach (var code in ClientMoneyCurrencies)
        {
            var exists = await db.SafekeepingAccounts.AnyAsync(
                row => row.TenantId == tenant.Id && row.Currency == code && row.IsActive, cancellationToken);
            if (exists)
            {
                continue;
            }

            var currency = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == code, cancellationToken);
            var row = SafekeepingAccount.Create(
                tenant.Id,
                currency,
                "Simulator Custody",
                $"{tenant.Name} Nominees",
                $"SIM-{tenant.Code.ToUpperInvariant()}-CUSTODY-{code}",
                SimulatorCustodianConnection);
            row.Created = TimeProvider.System.GetUtcNow();
            row.CreatedBy = CreatedBy;
            db.SafekeepingAccounts.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// One USD trade fee schedule (the demo brokerage and VTI are USD): 0.5% to 10,000 then 0.3%, minimum 10. It
    /// starts the day after the cut-off date at seed time, so it obeys trade-fees R10 and no seeded trade has a fee.
    /// </summary>
    private static async Task EnsureTradeFeeScheduleAsync(
        ApplicationDbContext db,
        int tenantId,
        CancellationToken cancellationToken)
    {
        const string currency = "USD";
        var exists = await db.TradeFeeSchedules.AnyAsync(
            row => row.TenantId == tenantId && row.QuoteCurrency == currency, cancellationToken);
        if (exists)
        {
            return;
        }

        var cutOff = await TradeFeeCutOff.ForAsync(
            db, tenantId, currency, TradeFeeCutOff.Today(TimeProvider.System), cancellationToken);
        var schedule = TradeFeeSchedule.Create(
            tenantId,
            Currency.Create(currency, "United States Dollar", 2),
            cutOff.AddDays(1),
            10m,
            null,
            [new FeeBand(0m, 0.5m), new FeeBand(10000m, 0.3m)],
            cutOff);
        schedule.Created = TimeProvider.System.GetUtcNow();
        schedule.CreatedBy = CreatedBy;
        db.TradeFeeSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCatalogAsync(
        ApplicationDbContext db,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var asOf = new DateOnly(2026, 1, 1);
        var fetchedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var item in Catalog)
        {
            var currency = Currency.Create(item.QuoteCurrency, item.QuoteCurrency, 2);
            var instrument = await EnsureInstrumentAsync(
                db, tenantId, item.Symbol, item.Name, currency, item.Active, cancellationToken);
            if (item.Price is not decimal price)
            {
                continue;
            }

            var stored = await db.InstrumentPrices.AnyAsync(
                row => row.InstrumentId == instrument.Id && row.AsOfDate == asOf,
                cancellationToken);
            if (stored)
            {
                continue;
            }

            db.InstrumentPrices.Add(new InstrumentPrice
            {
                InstrumentId = instrument.Id,
                Price = price,
                AsOfDate = asOf,
                Source = item.Symbol.EndsWith(SimulatedSuffix, StringComparison.Ordinal) ? SimulatorSource : "EODHD",
                FetchedAt = fetchedAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureReportGrantsAsync(
        ApplicationDbContext db,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var reports = await db.Reports
            .Where(row => DemoCatalog.ReportCodes.Contains(row.Code))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var reportId in reports)
        {
            var granted = await db.TenantReports.AnyAsync(
                row => row.TenantId == tenantId && row.ReportId == reportId,
                cancellationToken);
            if (granted)
            {
                continue;
            }

            db.TenantReports.Add(TenantReport.Grant(tenantId, reportId));
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<Instrument> EnsureInstrumentAsync(
        ApplicationDbContext db,
        int tenantId,
        string symbol,
        string name,
        Currency quoteCurrency,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var existing = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == symbol, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var instrument = Instrument.Create(tenantId, symbol, name, quoteCurrency);
        if (!enabled)
        {
            instrument.Disable();
        }

        instrument.Created = TimeProvider.System.GetUtcNow();
        instrument.CreatedBy = CreatedBy;
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);
        return instrument;
    }

    private static async Task EnsureCustomerAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        Tenant tenant,
        SeedFirm firm,
        CancellationToken cancellationToken)
    {
        var adviser = await db.DomainUsers.SingleOrDefaultAsync(
            row => row.TenantId == tenant.Id
                   && row.Role == UserRole.Adviser
                   && row.Email == firm.AdviserEmail,
            cancellationToken);
        if (adviser is null)
        {
            return;
        }

        if (await db.DomainUsers.AnyAsync(
                row => row.TenantId == tenant.Id && row.Email == firm.CustomerEmail, cancellationToken))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var publicId = Guid.NewGuid();
        var identityUser = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = firm.CustomerEmail,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };

        var result = await userManager.CreateAsync(
            identityUser, DevelopmentIdentitySeeder.AdviserPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var customer = User.Create(
            UserRole.Customer,
            firm.CustomerName,
            firm.CustomerEmail,
            identityUser.Id,
            tenant.Id,
            adviser.Id,
            publicId: publicId);
        customer.Created = TimeProvider.System.GetUtcNow();
        customer.CreatedBy = CreatedBy;
        db.DomainUsers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureAccountsAsync(
        ApplicationDbContext db,
        int tenantId,
        string customerEmail,
        CancellationToken cancellationToken)
    {
        var customer = await db.DomainUsers.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Email == customerEmail, cancellationToken);
        if (customer is null)
        {
            return;
        }

        var usd = Currency.Create("USD", "United States Dollar", 2);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

        await EnsureAccountAsync(
            db, tenantId, customer.Id, "Demo everyday bank", AccountType.Bank, CustodyMode.External, nzd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenantId, customer.Id, "Demo brokerage", AccountType.Brokerage, CustodyMode.External, usd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenantId, customer.Id, "Demo cash tin", AccountType.Cash, CustodyMode.External, nzd, closed: false,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenantId, customer.Id, "Demo closed other", AccountType.Other, CustodyMode.External, nzd, closed: true,
            cancellationToken);
        await EnsureAccountAsync(
            db, tenantId, customer.Id, DemoCustodialName, AccountType.Brokerage, CustodyMode.Custodial, usd,
            closed: false, cancellationToken);
        await EnsureDemoEverydayTransferAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoBrokerageOpeningAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoBrokerageFundingAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoBrokerageBuyAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoBrokerageStockInAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoBrokerageSplitAsync(db, tenantId, customer.Id, cancellationToken);
        await SyncDemoBrokerageHoldingAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoCustodialAsync(db, tenantId, customer.Id, cancellationToken);
        await EnsureDemoPortfolioAsync(db, tenantId, customer.Id, cancellationToken);
    }

    private const string DemoCustodialName = "Demo custodial brokerage";
    private const string DemoCustodialFundingReference = "TestSeed-DemoCustodial-Funding";
    private const string DemoCustodialBuyVtiReference = "TestSeed-DemoCustodial-BuyVti";
    private const string DemoCustodialBuyAaplReference = "TestSeed-DemoCustodial-BuyAapl";

    /// <summary>
    /// Custody modes §9.1: the Custodial demo account is funded with one TransferIn and buys VTI and AAPL at the
    /// seeded prices, then its Holdings rows are synced. Each transaction is idempotent on its reference.
    /// </summary>
    private static async Task EnsureDemoCustodialAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoCustodialAsync(db, customerId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var nzdt = TimeSpan.FromHours(13);
        if (!await ReferenceExistsAsync(db, account.Id, DemoCustodialFundingReference, cancellationToken))
        {
            var funding = Transaction.Post(
                tenantId,
                account.Id,
                TransactionType.TransferIn,
                20000m,
                account.Currency,
                new DateTimeOffset(2026, 1, 20, 10, 0, 0, nzdt),
                "Demo custodial funding",
                DemoCustodialFundingReference,
                TransactionSource.Seed,
                currentCashSum: await CashSumAsync(db, account.Id, cancellationToken),
                existingTransactionCount: 0);
            Stamp(funding, funding.CashLegs.Single());
            db.Transactions.Add(funding);
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureDemoCustodialBuyAsync(
            db, tenantId, account, "VTI", 40m, new DateTimeOffset(2026, 1, 20, 10, 30, 0, nzdt),
            DemoCustodialBuyVtiReference, cancellationToken);
        await EnsureDemoCustodialBuyAsync(
            db, tenantId, account, "AAPL", 20m, new DateTimeOffset(2026, 1, 20, 11, 0, 0, nzdt),
            DemoCustodialBuyAaplReference, cancellationToken);
        await SyncHoldingAsync(db, tenantId, account, "VTI", cancellationToken);
        await SyncHoldingAsync(db, tenantId, account, "AAPL", cancellationToken);
    }

    private static async Task EnsureDemoCustodialBuyAsync(
        ApplicationDbContext db,
        int tenantId,
        Account account,
        string symbol,
        decimal quantity,
        DateTimeOffset bookedAt,
        string reference,
        CancellationToken cancellationToken)
    {
        if (await ReferenceExistsAsync(db, account.Id, reference, cancellationToken))
        {
            return;
        }

        var instrument = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == symbol, cancellationToken);
        var asOf = DateOnly.FromDateTime(bookedAt.UtcDateTime);
        var stored = instrument is null
            ? null
            : await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == instrument.Id && row.AsOfDate <= asOf)
                .OrderByDescending(row => row.AsOfDate)
                .FirstOrDefaultAsync(cancellationToken);
        if (instrument is null || stored is null
            || !string.Equals(instrument.QuoteCurrency.Trim(), account.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var transaction = Transaction.PostBuy(
            tenantId,
            account.Id,
            account.Type,
            account.Currency,
            instrument.Id,
            quantity,
            Money.Create(stored.Price, instrument.QuoteCurrency.Trim()),
            fxRate: 1m,
            bookedAt,
            memo: "Demo custodial buy",
            reference: reference,
            TransactionSource.Seed,
            currentCashSum: await CashSumAsync(db, account.Id, cancellationToken));
        Stamp(transaction, transaction.CashLegs.Single());
        foreach (var leg in transaction.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Task<Account?> DemoCustodialAsync(
        ApplicationDbContext db,
        int customerId,
        CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == DemoCustodialName, cancellationToken);

    private static async Task EnsureDemoPortfolioAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var portfolio = await db.Portfolios.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Name == "Demo Growth", cancellationToken);
        if (portfolio is null)
        {
            var vti = await db.Instruments.SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
            if (vti is null)
            {
                return;
            }

            portfolio = Portfolio.Create(
                tenantId,
                "Demo Growth",
                Currency.Create("USD", "United States Dollar", 2),
                [(vti.Id, 100m)]);
            var created = TimeProvider.System.GetUtcNow();
            portfolio.Created = created;
            portfolio.CreatedBy = CreatedBy;
            db.Portfolios.Add(portfolio);
            await db.SaveChangesAsync(cancellationToken);
        }

        var account = await DemoCustodialAsync(db, customerId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var assigned = await db.AccountPortfolioAssignments.AnyAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
            cancellationToken);
        if (assigned)
        {
            return;
        }

        var assignment = AccountPortfolioAssignment.Assign(
            null, tenantId, account.Id, portfolio.Id, CreatedBy, TimeProvider.System.GetUtcNow());
        assignment.Created = TimeProvider.System.GetUtcNow();
        assignment.CreatedBy = CreatedBy;
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }

    private const string DemoBrokerageOpeningReference = "TestSeed-DemoBrokerage-OpeningHoldings";

    private static async Task EnsureDemoBrokerageOpeningAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo brokerage", cancellationToken);
        if (account is null)
        {
            return;
        }

        var exists = await ReferenceExistsAsync(db, account.Id, DemoBrokerageOpeningReference, cancellationToken);
        if (exists)
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.PostOpeningHoldings(
            tenantId,
            account.Id,
            account.Type,
            bookedAt,
            "Demo opening holdings",
            DemoBrokerageOpeningReference,
            TransactionSource.Seed,
            [OpeningSecurityLeg.Create(vti.Id, 10m, 2500.00m, vti.QuoteCurrency)]);
        var holding = Holding.Create(tenantId, account.Id, vti.Id, 10m, 2500.00m, vti.QuoteCurrency);
        var created = TimeProvider.System.GetUtcNow();
        transaction.Created = created;
        transaction.CreatedBy = CreatedBy;
        holding.Created = created;
        holding.CreatedBy = CreatedBy;
        foreach (var leg in transaction.SecurityLegs)
        {
            leg.Created = created;
            leg.CreatedBy = CreatedBy;
        }

        db.Transactions.Add(transaction);
        db.Holdings.Add(holding);
        await db.SaveChangesAsync(cancellationToken);
    }

    private const string DemoBrokerageFundingReference = "TestSeed-DemoBrokerage-Funding";
    private const string DemoBrokerageBuyReference = "TestSeed-DemoBrokerage-Buy";
    private const string DemoBrokerageStockInReference = "TestSeed-DemoBrokerage-StockIn";
    private const string DemoBrokerageSplitReference = "TestSeed-DemoBrokerage-Split";

    private static async Task EnsureDemoBrokerageFundingAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageFundingReference, cancellationToken))
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            1000m,
            account.Currency,
            bookedAt,
            "Demo brokerage funding",
            DemoBrokerageFundingReference,
            TransactionSource.Seed,
            currentCashSum: await CashSumAsync(db, account.Id, cancellationToken),
            existingTransactionCount: 1);
        Stamp(transaction, transaction.CashLegs.Single());
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageBuyAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageBuyReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        var bookedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(13));
        var asOf = DateOnly.FromDateTime(bookedAt.UtcDateTime);
        var stored = vti is null
            ? null
            : await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == vti.Id && row.AsOfDate <= asOf)
                .OrderByDescending(row => row.AsOfDate)
                .FirstOrDefaultAsync(cancellationToken);
        if (vti is null || stored is null)
        {
            return;
        }

        var quote = vti.QuoteCurrency.Trim();
        if (!string.Equals(quote, account.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var price = Money.Create(stored.Price, quote);

        var transaction = Transaction.PostBuy(
            tenantId,
            account.Id,
            account.Type,
            account.Currency,
            vti.Id,
            quantity: 2m,
            price,
            fxRate: 1m,
            bookedAt,
            memo: "Demo brokerage buy",
            reference: DemoBrokerageBuyReference,
            TransactionSource.Seed,
            currentCashSum: await CashSumAsync(db, account.Id, cancellationToken));
        Stamp(transaction, transaction.CashLegs.Single());
        foreach (var leg in transaction.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageStockInAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageStockInReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.PostStockIn(
            tenantId,
            account.Id,
            account.Type,
            vti.Id,
            quantity: 4m,
            cost: 800m,
            quoteCurrency: vti.QuoteCurrency,
            bookedAt,
            memo: "Demo brokerage stock in",
            reference: DemoBrokerageStockInReference,
            TransactionSource.Seed);
        Stamp(transaction);
        foreach (var leg in transaction.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDemoBrokerageSplitAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null || await ReferenceExistsAsync(db, account.Id, DemoBrokerageSplitReference, cancellationToken))
        {
            return;
        }

        var vti = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == "VTI", cancellationToken);
        if (vti is null)
        {
            return;
        }

        var legs = await (
            from leg in db.TransactionSecurityLegs
            where leg.AccountId == account.Id && leg.InstrumentId == vti.Id
            select new { leg.Quantity, leg.CostAmount }).ToListAsync(cancellationToken);
        if (legs.Count == 0)
        {
            return;
        }

        var quantity = legs.Sum(leg => leg.Quantity);
        var cost = legs.Sum(leg => leg.CostAmount);
        var bookedAt = new DateTimeOffset(2026, 1, 15, 11, 30, 0, TimeSpan.FromHours(13));
        var split = Transaction.PostSplit(
            tenantId,
            account.Id,
            account.Type,
            TransactionType.Split,
            vti.Id,
            newQuantity: quantity * 2m,
            quoteCurrency: vti.QuoteCurrency,
            bookedAt,
            memo: "Demo brokerage split",
            reference: DemoBrokerageSplitReference,
            TransactionSource.Seed,
            currentQuantity: quantity,
            currentCost: cost);
        Stamp(split);
        foreach (var leg in split.SecurityLegs)
        {
            Stamp(leg);
        }

        db.Transactions.Add(split);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SyncDemoBrokerageHoldingAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await DemoBrokerageAsync(db, customerId, cancellationToken);
        if (account is null)
        {
            return;
        }

        await SyncHoldingAsync(db, tenantId, account, "VTI", cancellationToken);
    }

    private static async Task SyncHoldingAsync(
        ApplicationDbContext db,
        int tenantId,
        Account account,
        string symbol,
        CancellationToken cancellationToken)
    {
        var instrument = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == symbol, cancellationToken);
        if (instrument is null)
        {
            return;
        }

        var legs = await (
            from leg in db.TransactionSecurityLegs
            where leg.AccountId == account.Id && leg.InstrumentId == instrument.Id
            select new { leg.Quantity, leg.CostAmount, leg.CostCurrency }).ToListAsync(cancellationToken);
        if (legs.Count == 0)
        {
            return;
        }

        var quantity = legs.Sum(leg => leg.Quantity);
        var cost = legs.Sum(leg => leg.CostAmount);
        var currency = legs[0].CostCurrency;
        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.InstrumentId == instrument.Id, cancellationToken);
        if (holding is null)
        {
            holding = Holding.Create(tenantId, account.Id, instrument.Id, quantity, cost, currency);
            Stamp(holding);
            db.Holdings.Add(holding);
        }
        else if (holding.ReplaceTotals(quantity, cost, currency))
        {
            db.Holdings.Remove(holding);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Task<Account?> DemoBrokerageAsync(
        ApplicationDbContext db,
        int customerId,
        CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo brokerage", cancellationToken);

    private static Task<bool> ReferenceExistsAsync(
        ApplicationDbContext db,
        int accountId,
        string reference,
        CancellationToken cancellationToken) =>
        db.Transactions.AnyAsync(
            row => row.Reference == reference
                   && (row.CashLegs.Any(leg => leg.AccountId == accountId)
                       || row.SecurityLegs.Any(leg => leg.AccountId == accountId)),
            cancellationToken);

    private static Task<decimal> CashSumAsync(
        ApplicationDbContext db,
        int accountId,
        CancellationToken cancellationToken) =>
        (from leg in db.TransactionCashLegs
         where leg.AccountId == accountId
         select leg.Amount).SumAsync(cancellationToken);

    private static void Stamp(params BaseAuditableEntity[] entities)
    {
        var created = TimeProvider.System.GetUtcNow();
        foreach (var entity in entities)
        {
            entity.Created = created;
            entity.CreatedBy = CreatedBy;
        }
    }

    private const string DemoEverydayBankTransferReference = "TestSeed-DemoEverydayBank-OpeningTransfer";

    private static async Task EnsureDemoEverydayTransferAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == "Demo everyday bank", cancellationToken);
        if (account is null)
        {
            return;
        }

        var exists = await ReferenceExistsAsync(db, account.Id, DemoEverydayBankTransferReference, cancellationToken);
        if (exists)
        {
            return;
        }

        var bookedAt = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(13));
        var transaction = Transaction.Post(
            tenantId,
            account.Id,
            TransactionType.TransferIn,
            100m,
            account.Currency,
            bookedAt,
            "Demo opening transfer",
            DemoEverydayBankTransferReference,
            TransactionSource.Seed,
            currentCashSum: 0m,
            existingTransactionCount: 0);
        var created = TimeProvider.System.GetUtcNow();
        transaction.Created = created;
        transaction.CreatedBy = CreatedBy;
        transaction.CashLegs.Single().Created = created;
        transaction.CashLegs.Single().CreatedBy = CreatedBy;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureAccountAsync(
        ApplicationDbContext db,
        int tenantId,
        int customerId,
        string name,
        AccountType type,
        CustodyMode custodyMode,
        Currency currency,
        bool closed,
        CancellationToken cancellationToken)
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(
            row => row.CustomerId == customerId && row.Name == name, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var depositReference = custodyMode == CustodyMode.Custodial
            ? DepositReference.Generate(CryptoRandomSymbols.Instance)
            : null;
        var account = Account.Create(tenantId, customerId, name, type, custodyMode, currency, depositReference);
        if (closed)
        {
            account.Close();
        }

        account.Created = TimeProvider.System.GetUtcNow();
        account.CreatedBy = CreatedBy;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
    }
}

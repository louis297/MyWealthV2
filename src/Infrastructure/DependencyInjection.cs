using MyWealthV2.Application.Rebalances;
using MyWealthV2.Infrastructure.CorporateActions;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Infrastructure.BankReconciliations;
using MyWealthV2.Application.BankReconciliations;
using Hangfire;
using Hangfire.SqlServer;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fx;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;
using MyWealthV2.Application.Reports;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Application.Broker;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.Currencies;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Interceptors;
using MyWealthV2.Infrastructure.Email;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using MyWealthV2.Infrastructure.Reports;
using MyWealthV2.Schema;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichSqlServerDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<ITenantIdAccessor, TenantIdAccessor>();
        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        builder.Services.AddSingleton<IDepositReferenceSource, RandomDepositReferenceSource>();
        builder.Services.AddScoped<FxRateStore>();
        builder.Services.AddScoped<IFxRate>(sp => sp.GetRequiredService<FxRateStore>());
        builder.Services.AddScoped<IFxRateStore>(sp => sp.GetRequiredService<FxRateStore>());
        builder.Services.AddHttpClient<IFxRateSource, FrankfurterClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.frankfurter.dev");
        });
        builder.Services.AddScoped<InstrumentPriceStore>();
        builder.Services.AddScoped<IInstrumentPriceStore>(sp => sp.GetRequiredService<InstrumentPriceStore>());
        builder.Services.AddScoped<IMarketData>(sp => sp.GetRequiredService<InstrumentPriceStore>());
        builder.Services.AddScoped<IActiveInstrumentCatalog, ActiveInstrumentCatalog>();
        builder.Services.AddHttpClient<IInstrumentPriceSource, EodhdClient>(client =>
        {
            client.BaseAddress = new Uri("https://eodhd.com/api");
        });

        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schema");
        builder.Services.AddSingleton<ISchemaScriptSource>(_ => new FileSchemaScriptSource(schemaDirectory));
        builder.Services.AddSingleton<ISchemaVersionStore>(_ => new SqlSchemaVersionStore(connectionString));
        builder.Services.AddSingleton<ISqlBatchExecutor>(_ => new SqlBatchExecutor(connectionString));
        builder.Services.AddScoped<SchemaApplicator>();

        builder.Services.AddAuthorization();

        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
                options.Password.RequiredLength = 8;
                // Hosted login lockout (identity-auth amendment 2026-11-13, lockout).
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<ApplicationDbContext>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTransient<IIdentityService, IdentityService>();
        builder.Services.AddScoped<ITokenRevocation, OpenIddictTokenRevocation>();
        builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();

        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = "Hangfire",
                PrepareSchemaIfNecessary = true
            }));
        builder.Services.Configure<ReportStorageOptions>(
            builder.Configuration.GetSection(ReportStorageOptions.SectionName));
        builder.Services.AddSingleton<IReportFileStore, ReportFileStore>();
        builder.Services.AddScoped<IReportGenerator, CustomerTransactionsReportGenerator>();
        builder.Services.AddScoped<IReportGenerator, AdviserBalancesReportGenerator>();
        builder.Services.AddScoped<IReportGenerator, BankReconciliationReportGenerator>();
        builder.Services.AddScoped<IReportGeneratorRegistry, ReportGeneratorRegistry>();
        builder.Services.AddScoped<ReportJob>();
        builder.Services.AddScoped<IReconcileClientMoneyAccount, ReconcileClientMoneyAccount>();
        builder.Services.AddScoped<BankReconciliationJob>();
        builder.Services.AddScoped<MyWealthV2.Infrastructure.Interest.InterestRunJob>();
        builder.Services.AddScoped<MyWealthV2.Application.Interest.Accruals.IAccountInterest, MyWealthV2.Application.Interest.Accruals.AccountInterest>();
        builder.Services.AddScoped<MyWealthV2.Application.Interest.Runs.IRunInterest, MyWealthV2.Application.Interest.Runs.RunInterest>();
        builder.Services.Configure<MyWealthV2.Application.Interest.Runs.InterestOptions>(
            builder.Configuration.GetSection(MyWealthV2.Application.Interest.Runs.InterestOptions.SectionName));
        builder.Services.AddSingleton<IReportScheduler, HangfireReportScheduler>();
        builder.Services.AddSingleton<IRebalanceScheduler, HangfireRebalanceScheduler>();

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<ApplicationDbContext>();
            });
    }

    /// <summary>
    /// Reads <c>MarketDataConnections</c> for <paramref name="host"/> and registers the configured connections
    /// (simulated-instruments R1–R3). Throws on a bad entry, so call it before <c>Build()</c>.
    /// </summary>
    public static void AddMarketDataConnections(this IHostApplicationBuilder builder, MarketDataHost host)
    {
        var connections = new ConfiguredMarketDataConnections(MarketDataConnectionsConfiguration.Read(builder.Configuration, host));
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton(connections.Connections);
        builder.Services.AddHttpClient(SimulatorMarketPrices.HttpClientName);
        builder.Services.AddSingleton<IMarketPricesAdapter, SimulatorMarketPrices>();
        builder.Services.AddScoped<IMarketPrices, MarketPricesDispatcher>();
        builder.Services.AddScoped<SyncMarketPrices>();
        builder.Services.AddScoped<ApplyCorporateActions>();
        builder.Services.AddScoped<ICorporateActionFeed, CorporateActionFeedDispatcher>();
        builder.Services.AddScoped<PullCorporateActions>();
        builder.Services.AddScoped<ProcessCorporateActionAnnouncements>();
        builder.Services.AddHttpClient(SimulatorCorporateActionFeed.HttpClientName);
        builder.Services.AddSingleton<ICorporateActionFeedAdapter, SimulatorCorporateActionFeed>();
        builder.Services.AddScoped<SyncMarketDataConnection>();
        if (host == MarketDataHost.Worker)
        {
            builder.Services.TryAddSingleton(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(Services.Database);
                Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");
                return new SqlRunLock(connectionString);
            });
            builder.Services.AddScoped<MarketDataSyncJob>();
            builder.Services.AddScoped<CorporateActionApplyJob>();
        }
    }

    /// <summary>
    /// Reads <c>BrokerConnections</c> for <paramref name="host"/> and registers <see cref="IBrokerConnections"/>
    /// (orders R2). Throws on a bad entry, so call it before <c>Build()</c>.
    /// </summary>
    public static void AddBrokerConnections(this IHostApplicationBuilder builder, BrokerConnectionHost host)
    {
        var connections = new ConfiguredBrokerConnections(BrokerConnectionsConfiguration.Read(builder.Configuration, host));
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton<IBrokerConnections>(connections);
        // ADR 0021 rule 3: each port is dispatched to the adapter its connection names.
        builder.Services.AddHttpClient(SimulatorBrokerOrders.HttpClientName);
        builder.Services.AddSingleton<IBrokerOrdersAdapter, SimulatorBrokerOrders>();
        builder.Services.AddHttpClient(SimulatorBrokerFeed.HttpClientName);
        builder.Services.AddSingleton<IBrokerFeedAdapter, SimulatorBrokerFeed>();
        builder.Services.AddScoped<IBrokerOrders, BrokerOrdersDispatcher>();
        builder.Services.AddScoped<IBrokerFeed, BrokerFeedDispatcher>();
        builder.Services.AddScoped<SendBrokerOrders>();
        builder.Services.AddScoped<PullExecutionReports>();
        builder.Services.AddScoped<ProcessExecutionReports>();
        builder.Services.AddScoped<PostOrderFills>();
        // Rebalances R6, R13–R17: a rebalance needs the broker connections.
        builder.Services.AddScoped<RebalancePlanning>();
        builder.Services.AddScoped<ProposeRebalance>();
        builder.Services.AddScoped<PlaceRebalanceWave>();
        builder.Services.AddScoped<AdvanceRebalances>();
        // Rebalances R12: the job proposes, so it needs the broker connections too.
        builder.Services.AddScoped<RebalanceJob>();
        if (host == BrokerConnectionHost.Worker)
        {
            // One run of a connection at a time (R19); the bank sync registers the same lock when it runs first.
            builder.Services.TryAddSingleton(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(Services.Database);
                Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");
                return new SqlRunLock(connectionString);
            });
            builder.Services.AddScoped<BrokerSyncJob>();
        }
    }

    /// <summary>
    /// Reads <c>CustodianConnections</c> for <paramref name="host"/> and registers <see cref="ICustodianConnections"/>
    /// (in-kind-transfers R2). Throws on a bad entry, so call it before <c>Build()</c>.
    /// </summary>
    public static void AddCustodianConnections(this IHostApplicationBuilder builder, MyWealthV2.Infrastructure.Custodian.CustodianConnectionHost host)
    {
        var connections = new MyWealthV2.Infrastructure.Custodian.ConfiguredCustodianConnections(
            MyWealthV2.Infrastructure.Custodian.CustodianConnectionsConfiguration.Read(builder.Configuration, host));
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton<MyWealthV2.Application.Custodian.ICustodianConnections>(connections);
        // ADR 0021 rule 3: each port is dispatched to the adapter its connection names.
        builder.Services.AddScoped<MyWealthV2.Application.Custodian.ICustodianFeed, MyWealthV2.Infrastructure.Custodian.CustodianFeedDispatcher>();
        builder.Services.AddScoped<MyWealthV2.Application.Custodian.ICustodianTransfers, MyWealthV2.Infrastructure.Custodian.CustodianTransfersDispatcher>();
        builder.Services.AddScoped<MyWealthV2.Application.Custodian.SendInKindTransfers>();
    }

    /// <summary>
    /// Reads <c>BankConnections</c> for <paramref name="host"/> and registers <see cref="IBankConnections"/>
    /// (deposit-matching R1). Throws on a bad entry, so call it before <c>Build()</c>.
    /// </summary>
    public static void AddBankConnections(this IHostApplicationBuilder builder, BankConnectionHost host)
    {
        var connections = new ConfiguredBankConnections(BankConnectionsConfiguration.Read(builder.Configuration, host));
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton<IBankConnections>(connections);
        builder.Services.AddHttpClient(SimulatorBankFeed.HttpClientName);
        builder.Services.AddSingleton<IBankFeedAdapter, SimulatorBankFeed>();
        builder.Services.AddHttpClient(SimulatorBankPayments.HttpClientName);
        builder.Services.AddSingleton<IBankPaymentsAdapter, SimulatorBankPayments>();
        builder.Services.AddScoped<IBankFeed, BankFeedDispatcher>();
        builder.Services.AddScoped<IBankPayments, BankPaymentsDispatcher>();
        builder.Services.AddScoped<PullBankStatements>();
        builder.Services.AddScoped<SendBankPayments>();
        builder.Services.AddScoped<ProcessStatementLines>();
        if (host == BankConnectionHost.Worker)
        {
            builder.Services.AddSingleton(sp =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(Services.Database);
                Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");
                return new SqlRunLock(connectionString);
            });
            builder.Services.AddScoped<BankStatementSyncJob>();
        }
    }
}

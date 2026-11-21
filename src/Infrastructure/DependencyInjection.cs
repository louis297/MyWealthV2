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
        builder.Services.AddSingleton<IReportScheduler, HangfireReportScheduler>();
        builder.Services.AddScoped<AccountRebalancer>();
        builder.Services.AddScoped<RebalanceJob>();
        builder.Services.AddSingleton<IRebalanceScheduler, HangfireRebalanceScheduler>();

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<ApplicationDbContext>();
            });
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

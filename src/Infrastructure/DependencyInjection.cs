using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Currencies;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Interceptors;
using MyWealthV2.Infrastructure.Data.Schema;
using MyWealthV2.Infrastructure.Email;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using MediatR;
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
        // IdentityHost reuses this method without MediatR; Web registers IMediator first.
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IMediator)))
        {
            builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        }

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichSqlServerDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        builder.Services.AddSingleton<MarketDataMock>();
        builder.Services.AddSingleton<IMarketData>(sp => sp.GetRequiredService<MarketDataMock>());
        builder.Services.AddSingleton<FxRateMock>();
        builder.Services.AddSingleton<IFxRate>(sp => sp.GetRequiredService<FxRateMock>());

        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schema");
        builder.Services.AddSingleton<ISchemaScriptSource>(_ => new FileSchemaScriptSource(schemaDirectory));
        builder.Services.AddScoped<ISchemaVersionStore, EfSchemaVersionStore>();
        builder.Services.AddScoped<ISqlBatchExecutor, EfSqlBatchExecutor>();
        builder.Services.AddScoped<SchemaApplicator>();

        builder.Services.AddAuthorization();

        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
                options.Password.RequiredLength = 8;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTransient<IIdentityService, IdentityService>();
        builder.Services.AddScoped<ITokenRevocation, OpenIddictTokenRevocation>();
        builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<ApplicationDbContext>();
            });
    }
}

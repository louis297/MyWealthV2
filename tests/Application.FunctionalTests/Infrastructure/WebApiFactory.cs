extern alias WebHost;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Hosting;
using MyWealthV2.Application.FunctionalTests.MarketData;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Reports;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(
    string connectionString,
    string? identityAuthority = null,
    HttpMessageHandler? identityBackchannel = null,
    string environment = "Development",
    IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<WebHost::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MyWealthV2.Shared.Services.Database}", connectionString);
        builder.UseSetting("Reports:StorageRoot", ReportStoragePaths.Root);
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        if (!string.IsNullOrWhiteSpace(identityAuthority))
        {
            builder.UseSetting("Identity:Authority", identityAuthority);
        }

        builder.ConfigureTestServices(services =>
        {
            // Every test reseeds Identity users. One PBKDF2 iteration keeps hashing cheap.
            // The count is stored in each hash, so IdentityHost still verifies these hashes.
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1);
            services.AddTransient<IStartupFilter, TestConnection>();

            services.RemoveAll<IReportScheduler>();
            services.AddSingleton<RecordingReportScheduler>();
            services.AddSingleton<IReportScheduler>(sp => sp.GetRequiredService<RecordingReportScheduler>());
            services.RemoveAll<IRebalanceScheduler>();
            services.AddSingleton<RecordingRebalanceScheduler>();
            services.AddSingleton<IRebalanceScheduler>(sp => sp.GetRequiredService<RecordingRebalanceScheduler>());
            services.RemoveAll<IDepositReferenceSource>();
            services.AddSingleton<ScriptedDepositReferenceSource>();
            services.AddSingleton<IDepositReferenceSource>(sp => sp.GetRequiredService<ScriptedDepositReferenceSource>());
            services.RemoveAll<IBankConnections>();
            services.AddSingleton<IBankConnections, FakeBankConnections>();
            services.RemoveAll<IBankFeed>();
            services.AddSingleton<InMemoryBankFeed>();
            services.AddSingleton<IBankFeed>(sp => sp.GetRequiredService<InMemoryBankFeed>());
            services.RemoveAll<IBankPayments>();
            services.AddSingleton<InMemoryBankPayments>();
            services.AddSingleton<IBankPayments>(sp => sp.GetRequiredService<InMemoryBankPayments>());
            services.RemoveAll<MarketDataConnections>();
            services.AddSingleton(MarketDataHttp.Connections);
            services.RemoveAll<IMarketPrices>();
            services.AddSingleton<InMemoryMarketPrices>();
            services.AddSingleton<IMarketPrices>(sp => sp.GetRequiredService<InMemoryMarketPrices>());
            services.TryAddScoped<SyncMarketPrices>();
            services.AddSingleton<CollidingAccountSaveInterceptor>();
            services.AddScoped<ISaveChangesInterceptor>(sp => sp.GetRequiredService<CollidingAccountSaveInterceptor>());

            if (identityBackchannel is not null && !string.IsNullOrWhiteSpace(identityAuthority))
            {
                var authority = identityAuthority.TrimEnd('/');
                var handler = identityBackchannel;
                services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(
                    new JwtBearerBackchannelPostConfigure(authority, handler));
            }
        });
    }
}

internal sealed class JwtBearerBackchannelPostConfigure(string authority, HttpMessageHandler handler)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        options.Authority = authority;
        options.MetadataAddress = authority + "/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false;
        options.BackchannelHttpHandler = handler;
        options.Backchannel = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = options.BackchannelTimeout,
            MaxResponseContentBufferSize = 1024 * 1024 * 10
        };
        options.Backchannel.DefaultRequestHeaders.UserAgent.ParseAdd("Microsoft ASP.NET Core JwtBearer handler");
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(options.Backchannel) { RequireHttps = false })
        {
            RefreshInterval = options.RefreshInterval,
            AutomaticRefreshInterval = options.AutomaticRefreshInterval
        };
    }
}

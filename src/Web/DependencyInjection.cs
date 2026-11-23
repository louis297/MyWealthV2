using Azure.Identity;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyWealthV2.Web.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    /// <summary>The <c>role</c> claim value identity issues for <c>UserRole.SimulatorOperator</c>.</summary>
    private const string SimulatorOperatorRoleClaim = "simulatorOperator";

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddScoped<CurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        builder.Services.AddScoped<IUser>(sp => sp.GetRequiredService<CurrentUser>());

        builder.Services.AddHttpContextAccessor();

        var authorization = builder.Services.AddAuthorizationBuilder();
        foreach (var policy in new[]
                 {
                     Policies.TenantsManage,
                     Policies.TenantsRead,
                     Policies.TenantAdminsManage,
                     Policies.AdvisersManage,
                     Policies.CustomersManage,
                     Policies.CustomersManageOwn,
                     Policies.UsersMe,
                     Policies.InstrumentsRead,
                     Policies.InstrumentsCreate,
                     Policies.InstrumentsManage,
                     Policies.AccountsRead,
                     Policies.AccountsCreate,
                     Policies.AccountsManage,
                     Policies.TransactionsRead,
                     Policies.TransactionsCreate,
                     Policies.StatementLinesManage,
                     Policies.BankReconciliationsManage,
                     Policies.NominatedBankAccountsRead,
                     Policies.NominatedBankAccountsManage,
                     Policies.NominatedBankAccountsVerify,
                     Policies.WithdrawalsRead,
                     Policies.WithdrawalsRequest,
                     Policies.WithdrawalsApprove,
                     Policies.AccountClosuresRead,
                     Policies.AccountClosuresRequest,
                     Policies.TransactionsAdjust,
                     Policies.PortfoliosRead,
                     Policies.PortfoliosManage,
                     Policies.FeesRead,
                     Policies.FeesManage,
                     Policies.InterestRatesRead,
                     Policies.InterestRatesManage,
                     Policies.InterestRunsRead,
                     Policies.InterestRunsStart,
                     Policies.CorporateActionsRead,
                     Policies.CorporateActionsManage,
                     Policies.OrdersRead,
                     Policies.OrdersPlace,
                     Policies.TradeSettlementsManage,
                     Policies.RebalancesRead,
                     Policies.RebalancesConfirm,
                     Policies.ReportsCustomerRead,
                     Policies.ReportsCustomerCreate,
                     Policies.ReportsBookRead,
                     Policies.ReportsBookCreate,
                     Policies.ReportsTenantRead,
                     Policies.ReportsTenantCreate
                 })
        {
            var name = policy;
            authorization.AddPolicy(name, policy => policy.AddRequirements(new PermissionRequirement(name)));
        }

        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, AnyPermissionHandler>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var authority = builder.Configuration["Identity:Authority"];
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                    options.RequireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                }

                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = AuthClaims.Subject;
                options.TokenValidationParameters.RoleClaimType = AuthClaims.Role;
                options.TokenValidationParameters.ValidateAudience = false;
                options.Events = new JwtBearerEvents
                {
                    // Simulator spec R3: a SimulatorOperator is never a webapi caller. Failing here makes every
                    // route answer 401 through OnChallenge, the default-authorized ones included, and never 403.
                    // The role claim is set by identity from the person row, and a role never changes.
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.HasClaim(AuthClaims.Role, SimulatorOperatorRoleClaim) == true)
                        {
                            context.Fail("The SimulatorOperator role is not a webapi caller.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        // fixes/utc-instants.md: every DateTimeOffset on the wire is UTC.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new UtcInstantJsonConverter()));

        // An unreadable body reaches ProblemDetailsExceptionHandler in every environment, not only Development.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

    }

    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder)
    {
        var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential());
        }
    }
}

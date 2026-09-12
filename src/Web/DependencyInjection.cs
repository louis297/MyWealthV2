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
                     Policies.TenantAdminsManage,
                     Policies.AdvisersManage,
                     Policies.CustomersManage,
                     Policies.CustomersManageOwn,
                     Policies.UsersMe
                 })
        {
            var name = policy;
            authorization.AddPolicy(name, policy => policy.AddRequirements(new PermissionRequirement(name)));
        }

        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

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
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        builder.Services.AddCors();
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

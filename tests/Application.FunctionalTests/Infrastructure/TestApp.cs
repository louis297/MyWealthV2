using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public static class TestApp
{
    private static string? _userId;
    private static List<string>? _roles;

    public static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        return await mediator.Send(request);
    }

    public static async Task SendAsync(IBaseRequest request)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        await mediator.Send(request);
    }

    public static string? GetUserId() => _userId;

    public static List<string>? GetRoles() => _roles;

    public static async Task<string> RunAsDefaultUserAsync()
    {
        return await RunAsUserAsync("test@local", "Testing1234!");
    }

    public static async Task<string> RunAsUserAsync(string userName, string password)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = userName, Email = userName };

        var result = await userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            _userId = user.Id;
            _roles = [];
            return _userId;
        }

        var errors = string.Join(Environment.NewLine, result.ToApplicationResult().Errors);

        throw new Exception($"Unable to create {userName}.{Environment.NewLine}{errors}");
    }

    public static async Task ResetState()
    {
        if (FunctionalTestSetup.DbResetter is not null)
        {
            await FunctionalTestSetup.DbResetter.ResetAsync();
        }

        _userId = null;
        _roles = null;
    }

    public static async Task<Tenant> CreateTenantAsync(string name, string code)
    {
        var tenant = Tenant.Create(name, code, Currency.Create("NZD", "New Zealand Dollar", 2));
        await AddAsync(tenant);
        return tenant;
    }

    public static async Task<User> CreatePersonAsync(
        UserRole role,
        string name,
        string email,
        string password,
        int? tenantId = null,
        int? adviserId = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var publicId = Guid.NewGuid();
        var identityUser = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };

        var created = await userManager.CreateAsync(identityUser, password);
        if (!created.Succeeded)
        {
            throw new Exception(string.Join(Environment.NewLine, created.ToApplicationResult().Errors));
        }

        var person = User.Create(role, name, email, identityUser.Id, tenantId, adviserId, publicId: publicId);
        db.DomainUsers.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    public static async Task<TEntity?> FindAsync<TEntity>(params object[] keyValues)
        where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.FindAsync<TEntity>(keyValues);
    }

    public static async Task AddAsync<TEntity>(TEntity entity)
        where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Add(entity);

        await context.SaveChangesAsync();
    }

    public static async Task<int> CountAsync<TEntity>() where TEntity : class
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Set<TEntity>().CountAsync();
    }

    public static async Task<bool> CheckPasswordAsync(string email, string password, string? tenantCode)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        User? person;
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            person = await db.DomainUsers.SingleOrDefaultAsync(
                row => row.Role == UserRole.SystemAdmin && row.Email == email);
        }
        else
        {
            var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Code == tenantCode);
            if (tenant is null)
            {
                return false;
            }

            person = await db.DomainUsers.SingleOrDefaultAsync(
                row => row.TenantId == tenant.Id && row.Email == email);
        }

        if (person is null)
        {
            return false;
        }

        var identityUser = await userManager.FindByIdAsync(person.IdentityUserId);
        return identityUser is not null && await userManager.CheckPasswordAsync(identityUser, password);
    }

    public static async Task<TokenResponse> IssueAccessTokenAsync(User person)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Tenant? tenant = null;
        if (person.TenantId is not null)
        {
            tenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Id == person.TenantId);
        }

        var signing = FunctionalTestSetup.Identity.Services
            .GetRequiredService<IOptions<OpenIddictServerOptions>>()
            .Value.SigningCredentials[0];
        var issuer = FunctionalTestSetup.Identity.ClientOptions.BaseAddress!.ToString();

        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(AuthClaims.Subject, person.PublicId.ToString()));
        identity.AddClaim(new Claim(AuthClaims.Email, person.Email));
        identity.AddClaim(new Claim(AuthClaims.Role, ToCamelCase(person.Role.ToString())));
        identity.AddClaim(new Claim(AuthClaims.TenantId, tenant?.PublicId.ToString() ?? string.Empty));
        identity.AddClaim(new Claim(AuthClaims.TenantCode, tenant?.Code ?? string.Empty));

        var accessToken = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            TokenType = "at+jwt",
            Subject = identity,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = signing
        });

        return new TokenResponse(accessToken, null);
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Users.EventHandlers;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class RevokeTokensIdempotencyTests
{
    [Test]
    public async Task RepeatUserDisabled_RevokesAnAlreadyRevokedSubject()
    {
        await using var provider = BuildProvider();
        var user = User.Create(
            UserRole.Customer, "Dee", "dee-revoke@example", "identity-dee", tenantId: 1, adviserId: 1);
        await CreateTokenAsync(provider, user.PublicId.ToString());
        var handler = new RevokeTokensOnUserDisabled(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.Handle(new UserDisabled(user), CancellationToken.None);
        await handler.Handle(new UserDisabled(user), CancellationToken.None);

        (await StatusesAsync(provider, user.PublicId.ToString())).ShouldBe(["revoked"]);
    }

    [Test]
    public async Task RepeatPasswordChange_RevokesAnAlreadyRevokedSubject()
    {
        await using var provider = BuildProvider();
        var user = User.Create(
            UserRole.Adviser, "Sam", "sam-revoke@example", "identity-sam", tenantId: 1);
        await CreateTokenAsync(provider, user.PublicId.ToString());
        var handler = new RevokeTokensOnUserPasswordChanged(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.Handle(new UserPasswordChanged(user), CancellationToken.None);
        await handler.Handle(new UserPasswordChanged(user), CancellationToken.None);

        (await StatusesAsync(provider, user.PublicId.ToString())).ShouldBe(["revoked"]);
    }

    [Test]
    public async Task RepeatTenantDisabled_RevokesAlreadyRevokedSubjects()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adminA, _) = await TwoTenantFixture.InsertTenantAdminsAsync(db, tenantA, tenantB);
        await CreateTokenAsync(provider, adminA.PublicId.ToString());
        var handler = new RevokeTokensOnTenantDisabled(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.Handle(new TenantDisabled(tenantA), CancellationToken.None);
        await handler.Handle(new TenantDisabled(tenantA), CancellationToken.None);

        (await StatusesAsync(provider, adminA.PublicId.ToString())).ShouldBe(["revoked"]);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(SchemaDatabaseSetup.ConnectionString));
        services.AddScoped<ITenantIdAccessor, TenantIdAccessor>();
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>());
        services.AddScoped<ITokenRevocation, OpenIddictTokenRevocation>();
        return services.BuildServiceProvider();
    }

    private static async Task CreateTokenAsync(ServiceProvider provider, string subject)
    {
        await using var scope = provider.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = subject,
            Status = OpenIddictConstants.Statuses.Valid,
            Type = OpenIddictConstants.TokenTypeHints.RefreshToken
        });
    }

    private static async Task<List<string?>> StatusesAsync(ServiceProvider provider, string subject)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database
            .SqlQuery<TokenStatus>($"SELECT [Status] FROM [OpenIddictTokens] WHERE [Subject] = {subject}")
            .Select(row => row.Status)
            .ToListAsync();
    }

    private sealed class TokenStatus
    {
        public string? Status { get; set; }
    }
}

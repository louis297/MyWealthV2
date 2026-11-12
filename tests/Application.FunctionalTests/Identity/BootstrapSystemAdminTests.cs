using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Hosting;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

// identity-auth amendment 2026-11-12: webapi creates the bootstrap SystemAdmin on start (A26),
// and fails before it listens when the configuration is incomplete.
public class BootstrapSystemAdminTests
{
    private const string Email = "owner@example.test";
    private const string Password = "Bootstrap1!";

    [SetUp]
    public async Task StartWithoutSystemAdmin()
    {
        await TestApp.ResetState();
        await FunctionalTestSetup.ReseedAsync();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityIds = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .Select(user => user.IdentityUserId)
            .ToListAsync();
        await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .ExecuteDeleteAsync();
        await db.Users.Where(user => identityIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    // Fixtures that do not reset state still expect the Development SystemAdmin.
    [TearDown]
    public async Task RestoreSeed()
    {
        await TestApp.ResetState();
        await FunctionalTestSetup.ReseedAsync();
    }

    [Test]
    public async Task WebApiStart_BothKeysSet_CreatesASystemAdminWhoCanSignInToBackOffice()
    {
        using (var factory = ProductionWebApi(new Dictionary<string, string>
               {
                   [SystemAdminBootstrap.EmailKey] = Email,
                   [SystemAdminBootstrap.PasswordKey] = Password
               }))
        {
            _ = factory.Server;
        }

        (await SystemAdminEmailsAsync()).ShouldBe([Email]);

        var login = await FunctionalTestSetup.BackOfficeOidc.SubmitLoginAsync(Email, Password, tenantCode: null);
        login.Code.ShouldNotBeNull();

        var tokens = await FunctionalTestSetup.BackOfficeOidc.SignInAsync(Email, Password, tenantCode: null);
        var response = await TenantHttp.Send(HttpMethod.Get, "/tenants", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task WebApiStart_OnlyEmailSet_FailsBeforeListeningAndCreatesNobody()
    {
        using var factory = ProductionWebApi(new Dictionary<string, string>
        {
            [SystemAdminBootstrap.EmailKey] = Email
        });

        var message = StartupFailure.Capture(factory);

        message.ShouldContain(SystemAdminBootstrap.PasswordKey);
        (await SystemAdminEmailsAsync()).ShouldBeEmpty();
    }

    private static WebApiFactory ProductionWebApi(IReadOnlyDictionary<string, string> settings) =>
        new(FunctionalTestSetup.ConnectionString, environment: "Production", settings: settings);

    private static async Task<List<string>> SystemAdminEmailsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .Select(user => user.Email)
            .ToListAsync();
    }
}

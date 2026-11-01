using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class DevelopmentSeedSecondFirmTests : TestBase
{
    [Test]
    public async Task SecondFirm_TenantAdminAndAdviser_SignInWithTheSharedPassword()
    {
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            await DevelopmentIdentitySeeder.SeedFirmsAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SecondTenantAdminEmail,
            DevelopmentIdentitySeeder.TenantAdminPassword,
            DevelopmentIdentitySeeder.SecondTenantCode);
        admin.AccessToken.ShouldNotBeNullOrWhiteSpace();

        var adviser = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentIdentitySeeder.SecondAdviserEmail,
            DevelopmentIdentitySeeder.AdviserPassword,
            DevelopmentIdentitySeeder.SecondTenantCode);
        adviser.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }
}

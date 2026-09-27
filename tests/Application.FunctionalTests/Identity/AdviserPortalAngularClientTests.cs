using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class AdviserPortalAngularClientTests
{
    [TearDown]
    public void RestoreFallbackClient()
    {
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString);
        _ = factory.Services;
    }

    [Test]
    public async Task ClientRow_Exists_WithoutPasswordGrant()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync("adviser-portal-angular");

        application.ShouldNotBeNull();
        var permissions = await manager.GetPermissionsAsync(application);
        permissions.ShouldNotContain(Permissions.GrantTypes.Password);
        var requirements = await manager.GetRequirementsAsync(application);
        requirements.ShouldContain(Requirements.Features.ProofKeyForCodeExchange);
    }

    [Test]
    public async Task Origins_DoNotChangeAdviserPortalRedirects()
    {
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString, new Dictionary<string, string>
        {
            ["Identity:PortalOrigins:0"] = "https://portal.test",
            ["Identity:AdviserPortalAngularOrigins:0"] = "https://angular.test"
        });
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var adviserPortal = await manager.FindByClientIdAsync("adviser-portal");
        adviserPortal.ShouldNotBeNull();
        var adviserPortalRedirects = await manager.GetRedirectUrisAsync(adviserPortal);
        adviserPortalRedirects.ShouldContain("https://portal.test/callback");
        adviserPortalRedirects.ShouldNotContain("https://angular.test/callback");

        var angular = await manager.FindByClientIdAsync("adviser-portal-angular");
        angular.ShouldNotBeNull();
        var angularRedirects = await manager.GetRedirectUrisAsync(angular);
        angularRedirects.ShouldContain("https://angular.test/callback");
        angularRedirects.ShouldNotContain("https://portal.test/callback");

        var permissions = await manager.GetPermissionsAsync(angular);
        permissions.ShouldNotContain(Permissions.GrantTypes.Password);
    }
}

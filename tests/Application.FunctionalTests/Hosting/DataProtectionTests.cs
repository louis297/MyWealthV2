using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.FunctionalTests.BackOffice;
using MyWealthV2.Application.FunctionalTests.Bff;
using MyWealthV2.Application.FunctionalTests.BffCustomer;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// production-hosting R9, R10: one Data Protection key folder and application name per public host.
public class DataProtectionTests
{
    private const string KeysPathKey = "DataProtection:KeysPath";

    private string _keys = null!;

    [SetUp]
    public void CreateFolder() => _keys = TestFolders.Create("keys");

    [TearDown]
    public void DeleteFolder() => TestFolders.Delete(_keys);

    [Test]
    public async Task AdviserBff_PersistsKeysToFolder_AndSecondInstanceReadsItsCookie()
    {
        using var first = AdviserBff(_keys);
        using (var client = first.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            await client.GetAsync("/bff/login");
        }

        Directory.GetFiles(_keys, "*.xml").ShouldNotBeEmpty();

        var cookie = SessionTicketFormat(first).Protect(Ticket("adviser@example.test"));

        using var second = AdviserBff(_keys);
        SessionTicketFormat(second).Unprotect(cookie).ShouldNotBeNull()
            .Principal.Identity!.Name.ShouldBe("adviser@example.test");

        var otherFolder = TestFolders.Create("keys");
        try
        {
            using var stranger = AdviserBff(otherFolder);
            SessionTicketFormat(stranger).Unprotect(cookie).ShouldBeNull();
        }
        finally
        {
            TestFolders.Delete(otherFolder);
        }
    }

    [Test]
    public void TwoHostsOnOneFolder_DoNotReadEachOthersCookies()
    {
        using var adviser = AdviserBff(_keys);
        var cookie = SessionTicketFormat(adviser).Protect(Ticket("adviser@example.test"));

        using var customer = new CustomerBffFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        customer.Settings[KeysPathKey] = _keys;

        SessionTicketFormat(customer).Unprotect(cookie).ShouldBeNull();
    }

    [Test]
    public void Identity_WithoutKeysPath_FailsStartup()
    {
        using var certificates = new TestCertificates();
        var settings = ProductionSettings.Identity(certificates);
        settings.Remove(KeysPathKey);
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString, settings, environment: "Production");

        StartupFailure.Capture(factory).ShouldContain(KeysPathKey);
    }

    [Test]
    public void AdviserBff_WithoutKeysPath_FailsStartup()
    {
        using var factory = new BffFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        factory.Settings.Remove(KeysPathKey);

        StartupFailure.Capture(factory).ShouldContain(KeysPathKey);
    }

    [Test]
    public void CustomerBff_WithoutKeysPath_FailsStartup()
    {
        using var factory = new CustomerBffFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        factory.Settings.Remove(KeysPathKey);

        StartupFailure.Capture(factory).ShouldContain(KeysPathKey);
    }

    [Test]
    public void BackOffice_WithoutKeysPath_FailsStartup()
    {
        using var factory = new BackOfficeFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        factory.Settings.Remove(KeysPathKey);

        StartupFailure.Capture(factory).ShouldContain(KeysPathKey);
    }

    private static BffFactory AdviserBff(string keys)
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        var factory = new BffFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            environment: "Production");
        factory.Settings[KeysPathKey] = keys;
        return factory;
    }

    private static ISecureDataFormat<AuthenticationTicket> SessionTicketFormat<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class =>
        factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme)
            .TicketDataFormat;

    private static AuthenticationTicket Ticket(string name) =>
        new(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test")),
            CookieAuthenticationDefaults.AuthenticationScheme);
}

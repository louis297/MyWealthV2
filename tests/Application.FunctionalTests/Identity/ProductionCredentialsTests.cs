using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using MyWealthV2.Application.FunctionalTests.Hosting;

namespace MyWealthV2.Application.FunctionalTests.Identity;

// production-hosting R1–R3: certificates and issuer from configuration outside Development.
public class ProductionCredentialsTests
{
    private TestCertificates _certificates = null!;

    [SetUp]
    public void CreateCertificates() => _certificates = new TestCertificates();

    [TearDown]
    public void Cleanup()
    {
        _certificates.Dispose();
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString);
        _ = factory.Services;
    }

    [Test]
    public async Task Discovery_ReturnsConfiguredIssuer_AndJwksHoldsSigningKey()
    {
        using var factory = CreateIdentity(ProductionSettings.Identity(_certificates));
        using var client = HttpsClient(factory);

        var discovery = await ReadJsonAsync(client, "/.well-known/openid-configuration");
        discovery.GetProperty("issuer").GetString().ShouldBe(ProductionSettings.Issuer);

        var jwks = await ReadJsonAsync(client, discovery.GetProperty("jwks_uri").GetString()!);
        var moduli = jwks.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("n").GetString())
            .ToList();
        moduli.ShouldContain(Base64UrlEncoder.Encode(_certificates.SigningModulus));
    }

    [TestCase("Identity:SigningCertificatePath")]
    [TestCase("Identity:EncryptionCertificatePath")]
    [TestCase("Identity:CertificatePassword")]
    [TestCase("Identity:Issuer")]
    public void MissingKey_FailsStartupNamingTheKey(string key)
    {
        var settings = ProductionSettings.Identity(_certificates);
        settings.Remove(key);
        using var factory = CreateIdentity(settings);

        var message = StartupFailure.Capture(factory);

        message.ShouldContain(key);
        message.ShouldNotContain(TestCertificates.Password);
    }

    [TestCase("Identity:SigningCertificatePath")]
    [TestCase("Identity:EncryptionCertificatePath")]
    public void MissingFile_FailsStartupNamingThePath(string key)
    {
        var settings = ProductionSettings.Identity(_certificates);
        var missing = Path.Combine(_certificates.Folder, "missing.pfx");
        settings[key] = missing;
        using var factory = CreateIdentity(settings);

        var message = StartupFailure.Capture(factory);

        message.ShouldContain(missing);
        message.ShouldNotContain(TestCertificates.Password);
    }

    [Test]
    public void WrongPassword_FailsStartupNamingThePathWithoutPassword()
    {
        var settings = ProductionSettings.Identity(_certificates);
        const string wrong = "WrongPfxPassword77";
        settings["Identity:CertificatePassword"] = wrong;
        using var factory = CreateIdentity(settings);

        var message = StartupFailure.Capture(factory);

        message.ShouldContain(_certificates.SigningPath);
        message.ShouldNotContain(wrong);
        message.ShouldNotContain(TestCertificates.Password);
    }

    [TestCase("http://id.example.test/")]
    [TestCase("https://id.example.test")]
    [TestCase("id.example.test/")]
    public void InvalidIssuer_FailsStartupNamingTheKey(string issuer)
    {
        var settings = ProductionSettings.Identity(_certificates);
        settings["Identity:Issuer"] = issuer;
        using var factory = CreateIdentity(settings);

        StartupFailure.Capture(factory).ShouldContain("Identity:Issuer");
    }

    private static IdentityFactory CreateIdentity(IReadOnlyDictionary<string, string> settings) =>
        new(FunctionalTestSetup.ConnectionString, settings, environment: "Production");

    private static HttpClient HttpsClient(IdentityFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// The production-hosting §8.1 keys for hosts started with UseEnvironment("Production").
internal static class ProductionSettings
{
    public const string Issuer = "https://id.example.test/";
    public const string TrustedNetwork = "10.20.0.0/24";
    public const string TrustedProxy = "10.20.0.5";
    public const string UntrustedAddress = "203.0.113.10";

    public static Dictionary<string, string> Identity(TestCertificates certificates) => new()
    {
        ["Identity:Issuer"] = Issuer,
        ["Identity:SigningCertificatePath"] = certificates.SigningPath,
        ["Identity:EncryptionCertificatePath"] = certificates.EncryptionPath,
        ["Identity:CertificatePassword"] = TestCertificates.Password,
        ["ForwardedHeaders:KnownNetworks:0"] = TrustedNetwork
    };

    // Defaults for the BFFs and back-office when a factory runs outside Development.
    public static Dictionary<string, string> PublicHost() => new()
    {
        ["ForwardedHeaders:KnownNetworks:0"] = TrustedNetwork
    };
}

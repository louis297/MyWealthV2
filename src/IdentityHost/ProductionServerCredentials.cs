using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MyWealthV2.IdentityHost;

// OpenIddict signing and encryption certificates and the issuer outside Development (production-hosting R1–R3).
public sealed record ProductionServerCredentials(
    Uri Issuer,
    X509Certificate2 SigningCertificate,
    X509Certificate2 EncryptionCertificate)
{
    public const string IssuerKey = "Identity:Issuer";
    public const string SigningCertificatePathKey = "Identity:SigningCertificatePath";
    public const string EncryptionCertificatePathKey = "Identity:EncryptionCertificatePath";
    public const string CertificatePasswordKey = "Identity:CertificatePassword";

    public static ProductionServerCredentials Load(IConfiguration configuration)
    {
        var issuer = ReadIssuer(configuration);
        var signingPath = Require(configuration, SigningCertificatePathKey);
        var encryptionPath = Require(configuration, EncryptionCertificatePathKey);
        var password = Require(configuration, CertificatePasswordKey);

        return new ProductionServerCredentials(
            issuer,
            LoadCertificate(SigningCertificatePathKey, signingPath, password),
            LoadCertificate(EncryptionCertificatePathKey, encryptionPath, password));
    }

    private static Uri ReadIssuer(IConfiguration configuration)
    {
        var value = Require(configuration, IssuerKey);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var issuer)
            || issuer.Scheme != Uri.UriSchemeHttps
            || !value.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"Configuration key '{IssuerKey}' must be an absolute https URL ending in '/'. Value: '{value}'.");
        }

        return issuer;
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration key '{key}' is required outside Development.");
        }

        return value;
    }

    private static X509Certificate2 LoadCertificate(string key, string path, string password)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Certificate file '{path}' from '{key}' does not exist.");
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            // The inner exception is dropped on purpose: never carry anything about the password onward.
            throw new InvalidOperationException(
                $"Certificate file '{path}' from '{key}' could not be opened with '{CertificatePasswordKey}'.");
        }
    }
}

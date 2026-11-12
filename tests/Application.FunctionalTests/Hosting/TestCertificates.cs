using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// Self-signed OpenIddict certificates for Production-environment tests (deployment §6 key usages).
// Written to a temporary folder per instance; no PFX is committed.
public sealed class TestCertificates : IDisposable
{
    public const string Password = "TestPfxPassword42";

    public TestCertificates()
    {
        Folder = TestFolders.Create("certs");
        SigningPath = Path.Combine(Folder, "signing.pfx");
        EncryptionPath = Path.Combine(Folder, "encryption.pfx");
        SigningModulus = Write(SigningPath, "CN=MyWealth signing", X509KeyUsageFlags.DigitalSignature);
        Write(EncryptionPath, "CN=MyWealth encryption", X509KeyUsageFlags.KeyEncipherment);
    }

    public string Folder { get; }
    public string SigningPath { get; }
    public string EncryptionPath { get; }
    public byte[] SigningModulus { get; }

    public void Dispose() => TestFolders.Delete(Folder);

    private static byte[] Write(string path, string subject, X509KeyUsageFlags usage)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, Password));
        return rsa.ExportParameters(false).Modulus!;
    }
}

internal static class TestFolders
{
    public static string Create(string purpose)
    {
        var path = Path.Combine(Path.GetTempPath(), "mywealth-tests", purpose + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

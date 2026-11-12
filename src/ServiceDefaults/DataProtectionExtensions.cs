using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

// Data Protection for the public hosts (production-hosting R9, R10).
public static class DataProtectionExtensions
{
    public const string KeysPathKey = "DataProtection:KeysPath";

    // Sets the host's own application name in every environment. Persists the key ring to
    // DataProtection:KeysPath when set; outside Development the key is required.
    // Keys are not encrypted at rest (deployment §11 known gap).
    public static TBuilder AddPublicHostDataProtection<TBuilder>(this TBuilder builder, string applicationName)
        where TBuilder : IHostApplicationBuilder
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(applicationName);

        var keysPath = builder.Configuration[KeysPathKey];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Configuration key '{KeysPathKey}' is required outside Development.");
            }

            return builder;
        }

        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        return builder;
    }
}

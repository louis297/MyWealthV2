using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyWealthV2.Application.Transactions;

internal static class TransactionRequestHash
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static string Compute(string method, string path, object businessBody)
    {
        var json = JsonSerializer.Serialize(businessBody, JsonOptions);
        var text = $"{method}\n{path}\n{json}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

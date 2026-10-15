using Microsoft.Extensions.Options;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Reports;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class ReportFileStore(IOptions<ReportStorageOptions> options) : IReportFileStore
{
    public async Task SaveAsync(string relativeKey, byte[] pdf, CancellationToken cancellationToken)
    {
        var path = Resolve(relativeKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, pdf, cancellationToken);
    }

    public Task DeleteIfExistsAsync(string relativeKey, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var path = Resolve(relativeKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReadAsync(string relativeKey, CancellationToken cancellationToken)
    {
        var path = Resolve(relativeKey);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private string Resolve(string relativeKey)
    {
        if (string.IsNullOrWhiteSpace(options.Value.StorageRoot))
        {
            throw new InvalidOperationException("Reports:StorageRoot is not configured.");
        }

        if (string.IsNullOrWhiteSpace(relativeKey)
            || relativeKey.Contains("..", StringComparison.Ordinal)
            || relativeKey.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(relativeKey))
        {
            throw new ArgumentException("Result key is not a report file.", nameof(relativeKey));
        }

        var parts = relativeKey.Split('/');
        if (parts.Length != 2
            || !Guid.TryParseExact(parts[0], "D", out var tenant)
            || !parts[1].EndsWith(".pdf", StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[1][..^4], "D", out var execution))
        {
            throw new ArgumentException("Result key is not a report file.", nameof(relativeKey));
        }

        return Path.Combine(
            options.Value.StorageRoot,
            tenant.ToString("D"),
            execution.ToString("D") + ".pdf");
    }
}

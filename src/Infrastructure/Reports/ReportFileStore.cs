using Microsoft.Extensions.Options;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Reports;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class ReportFileStore(IOptions<ReportStorageOptions> options) : IReportFileStore
{
    public Task SaveAsync(string relativeKey, byte[] pdf, CancellationToken cancellationToken)
    {
        _ = options;
        _ = relativeKey;
        _ = pdf;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public Task DeleteIfExistsAsync(string relativeKey, CancellationToken cancellationToken)
    {
        _ = relativeKey;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}

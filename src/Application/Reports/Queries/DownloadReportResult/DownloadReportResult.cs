using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Queries.DownloadReportResult;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead)]
public record DownloadReportResultQuery(Guid Id) : IRequest<ReportPdf>;

public sealed record ReportPdf(byte[] Content);

public class DownloadReportResultHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportFileStore files) : IRequestHandler<DownloadReportResultQuery, ReportPdf>
{
    public async Task<ReportPdf> Handle(DownloadReportResultQuery request, CancellationToken cancellationToken)
    {
        _ = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var execution = await db.ReportExecutions.AsNoTracking()
                            .SingleOrDefaultAsync(
                                row => row.PublicId == request.Id && row.TenantId == tenantId,
                                cancellationToken)
                        ?? throw new NotFoundException("ReportExecution", request.Id);
        if (execution.Status != "Succeeded" || execution.ResultStorageKey is null)
        {
            throw new NotFoundException("ReportExecution", request.Id);
        }

        var pdf = await files.ReadAsync(execution.ResultStorageKey, cancellationToken)
                  ?? throw new NotFoundException("ReportExecution", request.Id);
        return new ReportPdf(pdf);
    }
}

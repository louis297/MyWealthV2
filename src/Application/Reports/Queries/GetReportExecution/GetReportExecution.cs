using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Reports.Queries.GetReportExecution;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead + "," + Policies.ReportsTenantRead)]
public record GetReportExecutionQuery(Guid Id) : IRequest<ReportExecutionDto>;

public class GetReportExecutionQueryHandler : IRequestHandler<GetReportExecutionQuery, ReportExecutionDto>
{
    public Task<ReportExecutionDto> Handle(GetReportExecutionQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

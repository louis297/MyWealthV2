using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Queries.GetReportExecutions;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead)]
public record GetReportExecutionsQuery(int Page = 1, int PageSize = 20)
    : IRequest<PagedList<ReportExecutionDto>>;

public class GetReportExecutionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReportExecutionsQuery, PagedList<ReportExecutionDto>>
{
    public async Task<PagedList<ReportExecutionDto>> Handle(
        GetReportExecutionsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        var rows = await (
            from execution in db.ReportExecutions.AsNoTracking()
            join report in db.Reports.AsNoTracking() on execution.ReportId equals report.Id
            where execution.TenantId == tenantId
            orderby execution.Created descending, execution.Id descending
            select new
            {
                execution.PublicId,
                ReportPublicId = report.PublicId,
                execution.Status,
                execution.TriggerKind,
                execution.Created,
                execution.ParametersJson,
                execution.AsOf
            }).ToListAsync(cancellationToken);

        var items = rows.Select(row => new ReportExecutionDto
        {
            Id = row.PublicId,
            ReportId = row.ReportPublicId,
            Status = row.Status,
            TriggerKind = row.TriggerKind,
            Created = row.Created,
            Parameters = JsonSerializer.Deserialize<JsonElement>(row.ParametersJson),
            AsOf = row.AsOf
        }).ToList();

        return new PagedList<ReportExecutionDto>
        {
            Items = items
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = items.Count
        };
    }
}

using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Queries.GetReportExecution;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead + "," + Policies.ReportsTenantRead)]
public record GetReportExecutionQuery(Guid Id) : IRequest<ReportExecutionDto>;

public class GetReportExecutionQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReportExecutionQuery, ReportExecutionDto>
{
    public async Task<ReportExecutionDto> Handle(GetReportExecutionQuery request, CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();

        var row = await (
                      from execution in db.ReportExecutions.AsNoTracking()
                      join report in db.Reports.AsNoTracking() on execution.ReportId equals report.Id
                      where execution.PublicId == request.Id && execution.TenantId == tenantId
                      select new
                      {
                          execution.PublicId,
                          ReportPublicId = report.PublicId,
                          report.Scope,
                          execution.Status,
                          execution.TriggerKind,
                          execution.Created,
                          execution.ParametersJson,
                          execution.AsOf
                      }).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("ReportExecution", request.Id);

        if (!RolePermissions.Has(role, ReportScopes.ReadPolicy(row.Scope)))
        {
            throw new ForbiddenAccessException();
        }

        if (!ReportExecutionVisibility.TryReadParameters(row.ParametersJson, out var parameters))
        {
            throw new NotFoundException("ReportExecution", request.Id);
        }

        var customerAdvisers = new Dictionary<Guid, int?>();
        if (role == UserRole.Adviser
            && row.Scope != "Book"
            && ReportExecutionVisibility.TryReadCustomerId(parameters, out var customerId))
        {
            var customer = await db.Users.AsNoTracking()
                .Where(user => user.PublicId == customerId
                               && user.TenantId == tenantId
                               && user.Role == UserRole.Customer)
                .Select(user => new { user.PublicId, user.AdviserId })
                .SingleOrDefaultAsync(cancellationToken);
            if (customer is not null)
            {
                customerAdvisers[customer.PublicId] = customer.AdviserId;
            }
        }

        if (!ReportExecutionVisibility.IsVisible(row.Scope, parameters, role, currentUser, customerAdvisers))
        {
            throw new NotFoundException("ReportExecution", request.Id);
        }

        return new ReportExecutionDto
        {
            Id = row.PublicId,
            ReportId = row.ReportPublicId,
            Status = row.Status,
            TriggerKind = row.TriggerKind,
            Created = row.Created,
            Parameters = parameters,
            AsOf = row.AsOf
        };
    }
}

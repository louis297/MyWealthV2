using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Queries.GetReportExecutions;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead + "," + Policies.ReportsTenantRead)]
public record GetReportExecutionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Scope = null,
    Guid? ReportId = null) : IRequest<PagedList<ReportExecutionDto>>;

public class GetReportExecutionsQueryValidator : AbstractValidator<GetReportExecutionsQuery>
{
    public GetReportExecutionsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetReportExecutionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReportExecutionsQuery, PagedList<ReportExecutionDto>>
{
    public async Task<PagedList<ReportExecutionDto>> Handle(
        GetReportExecutionsQuery request,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        var scopes = ReadableScopes(role, request.Scope);

        var rows = await (
            from execution in db.ReportExecutions.AsNoTracking()
            join report in db.Reports.AsNoTracking() on execution.ReportId equals report.Id
            where execution.TenantId == tenantId
                && scopes.Contains(report.Scope)
                && (request.ReportId == null || report.PublicId == request.ReportId)
            orderby execution.Created descending, execution.Id descending
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
            }).ToListAsync(cancellationToken);

        var customers = await db.Users.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.Role == UserRole.Customer)
            .Select(row => new { row.PublicId, row.AdviserId })
            .ToDictionaryAsync(row => row.PublicId, row => row.AdviserId, cancellationToken);

        var items = new List<ReportExecutionDto>();
        foreach (var row in rows)
        {
            if (!ReportExecutionVisibility.TryReadParameters(row.ParametersJson, out var parameters))
            {
                continue;
            }

            if (!ReportExecutionVisibility.IsVisible(row.Scope, parameters, role, currentUser, customers))
            {
                continue;
            }

            items.Add(new ReportExecutionDto
            {
                Id = row.PublicId,
                ReportId = row.ReportPublicId,
                Status = row.Status,
                TriggerKind = row.TriggerKind,
                Created = row.Created,
                Parameters = parameters,
                AsOf = row.AsOf
            });
        }

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

    private static IReadOnlyList<string> ReadableScopes(UserRole role, string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return ReportScopes.All
                .Where(item => RolePermissions.Has(role, ReadPolicy(item)))
                .ToList();
        }

        if (!ReportScopes.IsKnown(scope))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(GetReportExecutionsQuery.Scope), ReportScopes.InvalidMessage)
            ]);
        }

        if (!RolePermissions.Has(role, ReadPolicy(scope)))
        {
            throw new ForbiddenAccessException();
        }

        return [scope];
    }

    private static string ReadPolicy(string scope) =>
        ReportScopes.ReadPolicy(scope);
}

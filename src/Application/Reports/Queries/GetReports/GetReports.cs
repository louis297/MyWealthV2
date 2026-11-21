using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Queries.GetReports;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead + "," + Policies.ReportsTenantRead)]
public record GetReportsQuery(string? Scope) : IRequest<IReadOnlyList<ReportCatalogDto>>;

public class GetReportsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReportsQuery, IReadOnlyList<ReportCatalogDto>>
{
    public async Task<IReadOnlyList<ReportCatalogDto>> Handle(
        GetReportsQuery request,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        var scopes = ReadableScopes(role, request.Scope);

        var rows = await (
            from grant in db.TenantReports.AsNoTracking()
            join report in db.Reports.AsNoTracking() on grant.ReportId equals report.Id
            where grant.TenantId == tenantId && report.IsActive && scopes.Contains(report.Scope)
            orderby report.Code
            select new
            {
                report.PublicId,
                report.Code,
                report.Name,
                report.Function,
                report.Scope,
                report.AllowedTriggers
            }).ToListAsync(cancellationToken);

        return rows.Select(row => new ReportCatalogDto
        {
            Id = row.PublicId,
            Code = row.Code,
            Name = row.Name,
            Function = row.Function,
            Scope = row.Scope,
            AllowedTriggers = TenantReportGrantDto.ParseTriggers(row.AllowedTriggers)
        }).ToList();
    }

    private static IReadOnlyList<string> ReadableScopes(Domain.Enums.UserRole role, string? scope)
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
                new ValidationFailure(nameof(GetReportsQuery.Scope), ReportScopes.InvalidMessage)
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

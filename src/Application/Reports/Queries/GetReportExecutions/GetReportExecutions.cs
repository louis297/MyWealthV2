using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Queries.GetReportExecutions;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead)]
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
            if (!TryReadParameters(row.ParametersJson, out var parameters))
            {
                continue;
            }

            if (!Visible(row.Scope, parameters, role, customers))
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

    private bool Visible(
        string scope,
        JsonElement parameters,
        UserRole role,
        IReadOnlyDictionary<Guid, int?> customers)
    {
        if (role == UserRole.TenantAdmin)
        {
            return true;
        }

        if (scope == "Book")
        {
            return role == UserRole.Adviser
                && parameters.TryGetProperty("adviserId", out var adviserId)
                && adviserId.TryGetGuid(out var id)
                && id == currentUser.PublicId;
        }

        if (!parameters.TryGetProperty("customerId", out var customerId)
            || !customerId.TryGetGuid(out var customerPublicId))
        {
            return false;
        }

        if (role == UserRole.Customer)
        {
            return customerPublicId == currentUser.PublicId;
        }

        return role == UserRole.Adviser
            && customers.TryGetValue(customerPublicId, out var adviserPersonId)
            && adviserPersonId == currentUser.Person?.Id;
    }

    private static bool TryReadParameters(string json, out JsonElement parameters)
    {
        try
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            parameters = default;
            return false;
        }

        return parameters.ValueKind == JsonValueKind.Object;
    }

    private static IReadOnlyList<string> ReadableScopes(UserRole role, string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return new[] { "Customer", "Book" }
                .Where(item => RolePermissions.Has(role, ReadPolicy(item)))
                .ToList();
        }

        if (scope is not ("Customer" or "Book"))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(GetReportExecutionsQuery.Scope), "Scope must be Customer or Book.")
            ]);
        }

        if (!RolePermissions.Has(role, ReadPolicy(scope)))
        {
            throw new ForbiddenAccessException();
        }

        return [scope];
    }

    private static string ReadPolicy(string scope) =>
        scope == "Book" ? Policies.ReportsBookRead : Policies.ReportsCustomerRead;
}

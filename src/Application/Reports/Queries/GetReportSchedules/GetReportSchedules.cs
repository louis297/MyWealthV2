using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Queries.GetReportSchedules;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead)]
public record GetReportSchedulesQuery(string? Scope) : IRequest<IReadOnlyList<ReportScheduleDto>>;

public class GetReportSchedulesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReportSchedulesQuery, IReadOnlyList<ReportScheduleDto>>
{
    public async Task<IReadOnlyList<ReportScheduleDto>> Handle(
        GetReportSchedulesQuery request,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        var scopes = ReadableScopes(role, request.Scope);

        var rows = await (
            from schedule in db.ReportSchedules.AsNoTracking()
            join report in db.Reports.AsNoTracking() on schedule.ReportId equals report.Id
            where schedule.TenantId == tenantId && scopes.Contains(report.Scope)
            orderby schedule.Created, schedule.Id
            select new
            {
                schedule.PublicId,
                ReportPublicId = report.PublicId,
                report.Code,
                schedule.ParametersJson,
                schedule.Cron,
                schedule.IsActive,
                schedule.RowVersion
            }).ToListAsync(cancellationToken);

        var customers = await db.Users.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.Role == UserRole.Customer)
            .Select(row => new { row.PublicId, row.AdviserId })
            .ToListAsync(cancellationToken);

        var items = new List<ReportScheduleDto>();
        foreach (var row in rows)
        {
            if (row.Code == CustomerTransactionsBinder.Code)
            {
                var parameters = JsonSerializer.Deserialize<JsonElement>(row.ParametersJson);
                var binding = CustomerTransactionsBinder.Bind(parameters);
                if (role == UserRole.Customer && currentUser.PublicId != binding.CustomerId)
                {
                    continue;
                }

                if (role == UserRole.Adviser)
                {
                    var customer = customers.SingleOrDefault(item => item.PublicId == binding.CustomerId);
                    if (customer is null || customer.AdviserId != currentUser.Person?.Id)
                    {
                        continue;
                    }
                }

                items.Add(ToDto(row.PublicId, row.ReportPublicId, parameters, row.Cron, row.IsActive, row.RowVersion));
                continue;
            }

            if (role == UserRole.Customer)
            {
                continue;
            }

            items.Add(ToDto(
                row.PublicId,
                row.ReportPublicId,
                JsonSerializer.Deserialize<JsonElement>(row.ParametersJson),
                row.Cron,
                row.IsActive,
                row.RowVersion));
        }

        return items;
    }

    private static ReportScheduleDto ToDto(
        Guid id,
        Guid reportId,
        JsonElement parameters,
        string cron,
        bool isActive,
        byte[] rowVersion) =>
        new()
        {
            Id = id,
            ReportId = reportId,
            Parameters = parameters,
            Cron = cron,
            IsActive = isActive,
            RowVersion = Convert.ToBase64String(rowVersion ?? [])
        };

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
                new ValidationFailure(nameof(GetReportSchedulesQuery.Scope), "Scope must be Customer or Book.")
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

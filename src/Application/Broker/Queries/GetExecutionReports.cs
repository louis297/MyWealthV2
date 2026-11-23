using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker.Queries;

/// <summary>An execution report item (docs/features/orders.md R30). <see cref="Kind"/> is null for a kind the adapter
/// did not know.</summary>
public sealed record ExecutionReportDto(
    Guid Id,
    string Connection,
    string OutsideId,
    string? Kind,
    string? ClientOrderId,
    string? BrokerOrderId,
    decimal? Quantity,
    decimal? Price,
    DateOnly? TradeDate,
    DateOnly? SettlementDate,
    string? ContractNote,
    string? FillOutsideId,
    DateOnly? SettledDate,
    string? Reason,
    DateTimeOffset ReceivedAt,
    string Status,
    string? SkipReason,
    DateTimeOffset? ProcessedAt,
    string? SettlementId,
    string? SettlementAccount,
    string? Currency,
    decimal? NetAmount,
    int? FillCount);

/// <summary>Orders R30: the stored execution reports, newest first. Platform data for a SystemAdmin.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetExecutionReportsQuery(int Page = 1, int PageSize = 20, string? Connection = null, string? Status = null)
    : IRequest<PagedList<ExecutionReportDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public class GetExecutionReportsQueryValidator : AbstractValidator<GetExecutionReportsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<ExecutionReportStatus>();

    public GetExecutionReportsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Status)
            .Must(status => GetExecutionReportsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetExecutionReportsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetExecutionReportsQuery, PagedList<ExecutionReportDto>>
{
    public async Task<PagedList<ExecutionReportDto>> Handle(GetExecutionReportsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ExecutionReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Connection))
        {
            var connection = request.Connection.Trim();
            query = query.Where(row => row.Connection == connection);
        }

        var statuses = GetExecutionReportsQuery.Names(request.Status)
            .Select(name => Enum.Parse<ExecutionReportStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<ExecutionReportDto>
        {
            Items = rows.Select(row => new ExecutionReportDto(
                row.PublicId, row.Connection, row.OutsideId, row.Kind?.ToString(), row.ClientOrderId, row.BrokerOrderId,
                row.Quantity, row.Price, row.TradeDate, row.SettlementDate, row.ContractNote, row.FillOutsideId,
                row.SettledDate, row.Reason, row.ReceivedAt, row.Status.ToString(), row.SkipReason?.ToString(),
                row.ProcessedAt, row.SettlementId, row.SettlementAccount, row.Currency?.Trim(), row.NetAmount, row.FillCount)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

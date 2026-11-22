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
    DateTimeOffset? ProcessedAt);

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

public class GetExecutionReportsQueryHandler : IRequestHandler<GetExecutionReportsQuery, PagedList<ExecutionReportDto>>
{
    public Task<PagedList<ExecutionReportDto>> Handle(GetExecutionReportsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

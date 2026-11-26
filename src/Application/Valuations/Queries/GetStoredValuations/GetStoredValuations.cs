using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Valuations.Queries.GetStoredValuations;

/// <summary>Valuation-run R1, R17, R19, R21: the stored valuations of one Account, newest day first.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetStoredValuationsQuery(
    Guid AccountId,
    int Page = 1,
    int PageSize = 20,
    string? From = null,
    string? To = null,
    bool? Complete = null) : IRequest<PagedList<StoredValuationSummaryDto>>;

public class GetStoredValuationsQueryHandler : IRequestHandler<GetStoredValuationsQuery, PagedList<StoredValuationSummaryDto>>
{
    public Task<PagedList<StoredValuationSummaryDto>> Handle(GetStoredValuationsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

/// <summary>Valuation-run R1, R18, R19, R21: the stored valuation of one Account and one day.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetStoredValuationQuery(Guid AccountId, string? Date) : IRequest<StoredValuationDto>;

public class GetStoredValuationQueryHandler : IRequestHandler<GetStoredValuationQuery, StoredValuationDto>
{
    public Task<StoredValuationDto> Handle(GetStoredValuationQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

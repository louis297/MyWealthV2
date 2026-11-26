using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Valuations.Queries.GetAccountValuation;

/// <summary>Account valuation R1, R20–R22: what one Account was worth at the end of one UTC day.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountValuationQuery(Guid AccountId, string? Date) : IRequest<AccountValuationDto>;

public class GetAccountValuationQueryHandler : IRequestHandler<GetAccountValuationQuery, AccountValuationDto>
{
    public Task<AccountValuationDto> Handle(GetAccountValuationQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankFeed.Queries.GetBankConnections;

/// <summary>Deposit matching R3: the configured bank connections, every entry on page 1.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetBankConnectionsQuery : IRequest<PagedList<BankConnectionDto>>;

public sealed record BankConnectionDto(string Name, string Adapter);

public class GetBankConnectionsQueryHandler
    : IRequestHandler<GetBankConnectionsQuery, PagedList<BankConnectionDto>>
{
    public Task<PagedList<BankConnectionDto>> Handle(GetBankConnectionsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

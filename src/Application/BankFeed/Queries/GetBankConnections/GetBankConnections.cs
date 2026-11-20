using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankFeed.Queries.GetBankConnections;

/// <summary>Deposit matching R3: the configured bank connections, every entry on page 1.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetBankConnectionsQuery : IRequest<PagedList<BankConnectionDto>>;

public sealed record BankConnectionDto(string Name, string Adapter);

public class GetBankConnectionsQueryHandler(IBankConnections connections)
    : IRequestHandler<GetBankConnectionsQuery, PagedList<BankConnectionDto>>
{
    private const int DefaultPageSize = 20;

    public Task<PagedList<BankConnectionDto>> Handle(GetBankConnectionsQuery request, CancellationToken cancellationToken)
    {
        var items = connections.All
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => new BankConnectionDto(entry.Name, entry.Adapter))
            .ToList();
        return Task.FromResult(new PagedList<BankConnectionDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(DefaultPageSize, items.Count),
            TotalCount = items.Count
        });
    }
}

using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NetWorth.Queries.GetFundsUnderManagement;

/// <summary>Valuation-history R1, R3–R13: the funds under management of the caller's scope for every day of a range.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetFundsUnderManagementQuery(Guid? TenantId = null, string? From = null, string? To = null)
    : IRequest<FundsUnderManagementDto>;

public class GetFundsUnderManagementQueryHandler
    : IRequestHandler<GetFundsUnderManagementQuery, FundsUnderManagementDto>
{
    public Task<FundsUnderManagementDto> Handle(GetFundsUnderManagementQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new FundsUnderManagementDto(string.Empty, default, default, []));
}

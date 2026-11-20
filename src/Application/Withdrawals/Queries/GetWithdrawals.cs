using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Withdrawals.Queries;

/// <summary>Withdrawals R26: the withdrawals of the Accounts in the caller's scope, newest requested first.</summary>
[Authorize(Policy = Policies.WithdrawalsRead)]
public sealed record GetWithdrawalsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    string? Status = null,
    Guid? TenantId = null) : IRequest<PagedList<WithdrawalDto>>;

/// <summary>Withdrawals R26: one withdrawal in the caller's scope.</summary>
[Authorize(Policy = Policies.WithdrawalsRead)]
public sealed record GetWithdrawalByIdQuery(Guid Id) : IRequest<WithdrawalDto>;

public class GetWithdrawalsQueryHandler : IRequestHandler<GetWithdrawalsQuery, PagedList<WithdrawalDto>>
{
    public Task<PagedList<WithdrawalDto>> Handle(GetWithdrawalsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetWithdrawalByIdQueryHandler : IRequestHandler<GetWithdrawalByIdQuery, WithdrawalDto>
{
    public Task<WithdrawalDto> Handle(GetWithdrawalByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

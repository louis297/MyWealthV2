using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NominatedBankAccounts.Queries;

/// <summary>Withdrawals R13: the caller's nominated bank accounts, newest added first.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsRead)]
public sealed record GetNominatedBankAccountsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? CustomerId = null,
    string? Status = null,
    string? Currency = null,
    Guid? TenantId = null) : IRequest<PagedList<NominatedBankAccountDto>>;

/// <summary>Withdrawals R13: one nominated bank account in the caller's scope.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsRead)]
public sealed record GetNominatedBankAccountByIdQuery(Guid Id) : IRequest<NominatedBankAccountDto>;

public class GetNominatedBankAccountsQueryHandler
    : IRequestHandler<GetNominatedBankAccountsQuery, PagedList<NominatedBankAccountDto>>
{
    public Task<PagedList<NominatedBankAccountDto>> Handle(
        GetNominatedBankAccountsQuery request,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetNominatedBankAccountByIdQueryHandler
    : IRequestHandler<GetNominatedBankAccountByIdQuery, NominatedBankAccountDto>
{
    public Task<NominatedBankAccountDto> Handle(GetNominatedBankAccountByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

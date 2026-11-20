using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.StatementLines.Queries.GetStatementLines;

/// <summary>Deposit matching R21, R22: the current tenant's statement lines, newest first.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed record GetStatementLinesQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? Currency = null,
    Guid? ClientMoneyAccountId = null,
    Guid? TenantId = null) : IRequest<PagedList<StatementLineDto>>;

public class GetStatementLinesQueryValidator : AbstractValidator<GetStatementLinesQuery>
{
}

public class GetStatementLinesQueryHandler : IRequestHandler<GetStatementLinesQuery, PagedList<StatementLineDto>>
{
    public Task<PagedList<StatementLineDto>> Handle(GetStatementLinesQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

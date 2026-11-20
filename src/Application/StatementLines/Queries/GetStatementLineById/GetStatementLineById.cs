using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.StatementLines.Queries.GetStatementLineById;

/// <summary>Deposit matching R21: one statement line of the current tenant.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed record GetStatementLineByIdQuery(Guid Id) : IRequest<StatementLineDto>;

public class GetStatementLineByIdQueryHandler : IRequestHandler<GetStatementLineByIdQuery, StatementLineDto>
{
    public Task<StatementLineDto> Handle(GetStatementLineByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

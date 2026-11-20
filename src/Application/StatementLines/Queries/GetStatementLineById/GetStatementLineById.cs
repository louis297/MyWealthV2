using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.StatementLines.Queries.GetStatementLineById;

/// <summary>Deposit matching R21: one statement line of the current tenant.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed record GetStatementLineByIdQuery(Guid Id) : IRequest<StatementLineDto>;

public class GetStatementLineByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetStatementLineByIdQuery, StatementLineDto>
{
    public async Task<StatementLineDto> Handle(GetStatementLineByIdQuery request, CancellationToken cancellationToken)
    {
        var line = await StatementLineReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        return (await StatementLineReader.ToDtosAsync(db, [line], cancellationToken)).Single();
    }
}

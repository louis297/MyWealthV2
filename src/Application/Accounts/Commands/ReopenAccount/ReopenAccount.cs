using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Commands.ReopenAccount;

[Authorize(Policy = Policies.AccountsManage)]
public class ReopenAccountCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class ReopenAccountCommandValidator : AbstractValidator<ReopenAccountCommand>
{
    public ReopenAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class ReopenAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ReopenAccountCommand>
{
    public async Task Handle(ReopenAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                      ?? throw new NotFoundException("Account", request.Id);

        var expected = Convert.ToBase64String(account.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        account.Reopen();
        await db.SaveChangesAsync(cancellationToken);
    }
}

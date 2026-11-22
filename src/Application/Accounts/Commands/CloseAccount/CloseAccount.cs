using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Accounts.Commands.CloseAccount;

[Authorize(Policy = Policies.AccountsManage)]
public class CloseAccountCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class CloseAccountCommandValidator : AbstractValidator<CloseAccountCommand>
{
    public CloseAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class CloseAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CloseAccountCommand>
{
    public async Task Handle(CloseAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                      ?? throw new NotFoundException("Account", request.Id);

        var expected = Convert.ToBase64String(account.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        // Account closure R22: an Open Custodial account is closed only by an account closure, for every caller.
        // A Closed one is still 204 (accounts R12).
        if (account.CustodyMode == CustodyMode.Custodial && account.Status == AccountStatus.Open)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "A Custodial account is closed with an account closure.")
            ]);
        }

        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            var cashSum = await TransactionCash.SumAsync(db, account.Id, token);
            if (cashSum != 0m)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, "Cannot close an account while the cash balance is not zero.")
                ]);
            }

            if (await db.Holdings.AnyAsync(row => row.AccountId == account.Id, token))
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, "Cannot close an account while a holding remains.")
                ]);
            }

            account.Close();
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }
}

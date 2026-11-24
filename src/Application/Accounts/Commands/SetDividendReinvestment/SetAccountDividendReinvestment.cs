using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Accounts.Commands.SetDividendReinvestment;

/// <summary>Dividends R17: the standing reinvestment election of a Custodial account. It applies to every dividend
/// that goes ex after it is set.</summary>
[Authorize(Policy = Policies.AccountsManage)]
public sealed class SetAccountDividendReinvestmentCommand : IRequest
{
    public const string ExternalMessage = "Dividends are reinvested only on a Custodial account.";

    [JsonIgnore]
    public Guid AccountId { get; set; }

    public bool? Reinvest { get; init; }

    public string? RowVersion { get; init; }
}

public class SetAccountDividendReinvestmentCommandValidator : AbstractValidator<SetAccountDividendReinvestmentCommand>
{
    public SetAccountDividendReinvestmentCommandValidator()
    {
        RuleFor(command => command.Reinvest).NotNull();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class SetAccountDividendReinvestmentCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<SetAccountDividendReinvestmentCommand>
{
    public async Task Handle(SetAccountDividendReinvestmentCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
                      ?? throw new NotFoundException("Account", request.AccountId);
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(SetAccountDividendReinvestmentCommand.AccountId),
                    SetAccountDividendReinvestmentCommand.ExternalMessage)
            ]);
        }

        if (account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        if (!string.Equals(Convert.ToBase64String(account.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        account.SetDividendReinvestment(request.Reinvest!.Value, actor, time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}

using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.NominatedBankAccounts.Commands;

/// <summary>Withdrawals R4: add a Customer's own bank account, Pending until a TenantAdmin checks it.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsManage)]
public sealed class AddNominatedBankAccountCommand : IRequest<Guid>
{
    public const string DuplicateMessage = "This bank account is already nominated for this customer.";

    public Guid? CustomerId { get; init; }

    public string? Currency { get; init; }

    public string? BankName { get; init; }

    public string? AccountName { get; init; }

    public string? AccountNumber { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public string? Status { get; init; }

    /// <summary>Refused when present (R1, R4).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public Guid? Id { get; init; }
}

public class AddNominatedBankAccountCommandValidator : AbstractValidator<AddNominatedBankAccountCommand>
{
    public AddNominatedBankAccountCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty();
        RuleFor(command => command.BankName)
            .Must(value => value?.Trim().Length is >= 1 and <= NominatedBankAccount.BankNameMaxLength)
            .WithMessage($"Bank name must be 1 to {NominatedBankAccount.BankNameMaxLength} characters.");
        RuleFor(command => command.AccountName)
            .Must(value => value?.Trim().Length is >= 1 and <= NominatedBankAccount.AccountNameMaxLength)
            .WithMessage($"Account name must be 1 to {NominatedBankAccount.AccountNameMaxLength} characters.");
        RuleFor(command => command.AccountNumber)
            .Must(ClientMoneyAccount.IsValidAccountNumber)
            .WithMessage(
                $"Account number must be 1 to {NominatedBankAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        RuleFor(command => command.Status).Null().WithMessage("Status cannot be set.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Id).Null().WithMessage("Id cannot be set.");
    }
}

public class AddNominatedBankAccountCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time) : IRequestHandler<AddNominatedBankAccountCommand, Guid>
{
    public async Task<Guid> Handle(AddNominatedBankAccountCommand request, CancellationToken cancellationToken)
    {
        var customer = await NominatedBankAccountReader.Customers(db, currentUser).AsNoTracking()
                           .SingleOrDefaultAsync(row => row.PublicId == request.CustomerId, cancellationToken)
                       ?? throw new NotFoundException("Customer", request.CustomerId!.Value);
        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot add a bank account while the customer is disabled. Enable the customer first.");
        }

        var currency = catalog.TryGet(request.Currency!.Trim());
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(AddNominatedBankAccountCommand.Currency), "Currency must be an enabled catalog code.")
            ]);
        }

        var actor = NominatedBankAccountReader.ActorId(currentUser);
        var row = NominatedBankAccount.Add(
            customer.TenantId!.Value, customer.Id, currency, request.BankName!, request.AccountName!, request.AccountNumber!,
            actor, time.GetUtcNow());

        // R5: one Pending or Verified row per Customer, number and currency; the filtered unique index is the backstop.
        if (await IsNominatedAsync(row, cancellationToken))
        {
            throw Duplicate();
        }

        db.NominatedBankAccounts.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ValidationException)
        {
            db.ClearTrackedChanges();
            if (await IsNominatedAsync(row, cancellationToken))
            {
                throw Duplicate();
            }

            throw;
        }

        return row.PublicId;
    }

    private Task<bool> IsNominatedAsync(NominatedBankAccount row, CancellationToken cancellationToken) =>
        db.NominatedBankAccounts.AsNoTracking().AnyAsync(existing =>
            existing.TenantId == row.TenantId
            && existing.CustomerId == row.CustomerId
            && existing.AccountNumberKey == row.AccountNumberKey
            && existing.Currency == row.Currency
            && (existing.Status == NominatedBankAccountStatus.Pending || existing.Status == NominatedBankAccountStatus.Verified),
            cancellationToken);

    private static ValidationException Duplicate() =>
        new([new ValidationFailure(nameof(AddNominatedBankAccountCommand.AccountNumber), AddNominatedBankAccountCommand.DuplicateMessage)]);
}

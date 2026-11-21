using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.NominatedBankAccounts.Commands;

/// <summary>Withdrawals R7–R11: a TenantAdmin other than the adder checked the evidence.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsVerify)]
public sealed class VerifyNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R7–R11: a TenantAdmin other than the adder refuses it.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsVerify)]
public sealed class RejectNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R7, R8, R11, R14: no longer used.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsManage)]
public sealed class RemoveNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

public class VerifyNominatedBankAccountCommandValidator : AbstractValidator<VerifyNominatedBankAccountCommand>
{
    public VerifyNominatedBankAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(NominatedBankAccountReader.IsRequiredNote).WithMessage(NominatedBankAccountReader.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class RejectNominatedBankAccountCommandValidator : AbstractValidator<RejectNominatedBankAccountCommand>
{
    public RejectNominatedBankAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(NominatedBankAccountReader.IsRequiredNote).WithMessage(NominatedBankAccountReader.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class RemoveNominatedBankAccountCommandValidator : AbstractValidator<RemoveNominatedBankAccountCommand>
{
    public RemoveNominatedBankAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(NominatedBankAccountReader.IsRequiredNote).WithMessage(NominatedBankAccountReader.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class VerifyNominatedBankAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<VerifyNominatedBankAccountCommand>
{
    public Task Handle(VerifyNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        NominatedBankAccountChange.ApplyAsync(db, currentUser, request.Id, request.RowVersion, checkCustomer: true,
            (row, actor) => row.Verify(actor, request.Note!, time.GetUtcNow()), cancellationToken);
}

public class RejectNominatedBankAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<RejectNominatedBankAccountCommand>
{
    public Task Handle(RejectNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        NominatedBankAccountChange.ApplyAsync(db, currentUser, request.Id, request.RowVersion, checkCustomer: false,
            (row, actor) => row.Reject(actor, request.Note!, time.GetUtcNow()), cancellationToken);
}

public class RemoveNominatedBankAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<RemoveNominatedBankAccountCommand>
{
    public Task Handle(RemoveNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        NominatedBankAccountChange.ApplyAsync(db, currentUser, request.Id, request.RowVersion, checkCustomer: false,
            (row, actor) => row.Remove(actor, request.Note!, time.GetUtcNow()), cancellationToken);
}

/// <summary>R8, R10: find in scope, check the row version (and the Customer for a verify), change, save.</summary>
internal static class NominatedBankAccountChange
{
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        string? rowVersion,
        bool checkCustomer,
        Action<NominatedBankAccount, int> change,
        CancellationToken cancellationToken)
    {
        var row = await NominatedBankAccountReader.FindAsync(db, currentUser, id, cancellationToken);
        NominatedBankAccountReader.CheckRowVersion(row, rowVersion);
        var actor = NominatedBankAccountReader.ActorId(currentUser);
        if (checkCustomer)
        {
            var customer = await db.Users.AsNoTracking().SingleAsync(user => user.Id == row.CustomerId, cancellationToken);
            if (customer.Status == UserStatus.Disabled)
            {
                throw new TargetDisabledException(
                    "user",
                    customer.PublicId,
                    "User is disabled",
                    "Cannot verify a bank account while the customer is disabled. Enable the customer first.");
            }
        }

        NominatedBankAccountReader.Guard(() => change(row, actor));
        await NominatedBankAccountReader.SaveAsync(db, cancellationToken);
    }
}

using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>In-kind transfers R30: the body of every action on a transfer.</summary>
public abstract class InKindTransferActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>R31: a TenantAdmin other than the requester approves the transfer.</summary>
[Authorize(Policy = Policies.InKindTransfersApprove)]
public sealed class ApproveInKindTransferCommand : InKindTransferActionCommand;

/// <summary>R31: a TenantAdmin other than the requester refuses the transfer, with a note.</summary>
[Authorize(Policy = Policies.InKindTransfersApprove)]
public sealed class RejectInKindTransferCommand : InKindTransferActionCommand;

/// <summary>R32: the transfer is withdrawn before the custodian was asked.</summary>
[Authorize(Policy = Policies.InKindTransfersRequest)]
public sealed class CancelInKindTransferCommand : InKindTransferActionCommand;

/// <summary>R33: a TenantAdmin ends a transfer the custodian was asked to make, with a note.</summary>
[Authorize(Policy = Policies.InKindTransfersApprove)]
public sealed class SetAsideInKindTransferCommand : InKindTransferActionCommand;

internal static class InKindTransferActionRules
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsOptionalNote(string? note) => (note?.Trim().Length ?? 0) <= InKindTransfer.NoteMaxLength;

    public static bool IsRequiredNote(string? note) => note?.Trim().Length is >= 1 and <= InKindTransfer.NoteMaxLength;

    public static void Rules<T>(AbstractValidator<T> validator, bool noteRequired)
        where T : InKindTransferActionCommand
    {
        validator.RuleFor(command => command.RowVersion).NotEmpty();
        validator.RuleFor(command => command.Note)
            .Must(noteRequired ? IsRequiredNote : IsOptionalNote)
            .WithMessage(NoteMessage);
        validator.RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class ApproveInKindTransferCommandValidator : AbstractValidator<ApproveInKindTransferCommand>
{
    public ApproveInKindTransferCommandValidator() => InKindTransferActionRules.Rules(this, noteRequired: false);
}

public class RejectInKindTransferCommandValidator : AbstractValidator<RejectInKindTransferCommand>
{
    public RejectInKindTransferCommandValidator() => InKindTransferActionRules.Rules(this, noteRequired: true);
}

public class CancelInKindTransferCommandValidator : AbstractValidator<CancelInKindTransferCommand>
{
    public CancelInKindTransferCommandValidator() => InKindTransferActionRules.Rules(this, noteRequired: false);
}

public class SetAsideInKindTransferCommandValidator : AbstractValidator<SetAsideInKindTransferCommand>
{
    public SetAsideInKindTransferCommandValidator() => InKindTransferActionRules.Rules(this, noteRequired: true);
}

/// <summary>Finds, guards and saves a transfer for an action (R30).</summary>
internal static class InKindTransferChange
{
    public static async Task<InKindTransfer> FindAsync(
        IApplicationDbContext db, ICurrentUser currentUser, Guid id, string? rowVersion, CancellationToken cancellationToken)
    {
        var transfer = await InKindTransferReader.Scope(db, currentUser)
                           .SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken)
                       ?? throw new NotFoundException("In-kind transfer", id);
        if (!string.Equals(Convert.ToBase64String(transfer.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        return transfer;
    }

    public static int ActorId(ICurrentUser currentUser) => currentUser.Person?.Id ?? throw new ForbiddenAccessException();

    /// <summary>R30–R33: a wrong status, the requester deciding, or a cancel after an attempt is 400 at <c>Status</c>.</summary>
    public static void Guard(Action change)
    {
        try
        {
            change();
        }
        catch (InKindTransferStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }
    }

    public static async Task SaveAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
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

public class ApproveInKindTransferCommandHandler(
    IApplicationDbContext db, ICurrentUser currentUser, ICustodianConnections connections, TimeProvider time)
    : IRequestHandler<ApproveInKindTransferCommand>
{
    public async Task Handle(ApproveInKindTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await InKindTransferChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = InKindTransferChange.ActorId(currentUser);
        await db.ExecuteInTransactionAsync(async token =>
        {
            // R31: the checks run under the Account lock, as the request's did.
            await db.LockAccountAsync(transfer.AccountId, token);
            // R30, R31: the status and the second person are checked before the account and the safekeeping account.
            if (transfer.Status != InKindTransferStatus.Requested)
            {
                throw new ValidationException([new ValidationFailure("Status", $"This transfer is {transfer.Status} and cannot be approved.")]);
            }

            if (actor == transfer.RequestedByUserId)
            {
                throw new ValidationException([new ValidationFailure("Status", InKindTransfer.FourEyesMessage)]);
            }

            var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == transfer.AccountId, token);
            await InKindTransferRules.EnsureAccountTakesTransfersAsync(db, account, token);
            var instrument = await db.Instruments.AsNoTracking().SingleAsync(row => row.Id == transfer.InstrumentId, token);
            var safekeeping = await InKindTransferRules.SafekeepingAccountAsync(
                db, connections, transfer.TenantId, transfer.Currency.Trim(), instrument.Symbol, token);
            if (transfer.Direction == TransferDirection.In && !instrument.IsActive)
            {
                throw InKindTransferRules.Error("InstrumentId", InKindTransferRules.InstrumentDisabledMessage);
            }

            InKindTransferChange.Guard(() => transfer.Approve(actor, time.GetUtcNow(), request.Note, safekeeping.Id));
            await InKindTransferChange.SaveAsync(db, token);
        }, cancellationToken);
    }
}

public class RejectInKindTransferCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<RejectInKindTransferCommand>
{
    public async Task Handle(RejectInKindTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await InKindTransferChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = InKindTransferChange.ActorId(currentUser);
        InKindTransferChange.Guard(() => transfer.Reject(actor, time.GetUtcNow(), request.Note!));
        await InKindTransferChange.SaveAsync(db, cancellationToken);
    }
}

public class CancelInKindTransferCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<CancelInKindTransferCommand>
{
    public async Task Handle(CancelInKindTransferCommand request, CancellationToken cancellationToken)
    {
        // R32: the transfer's RowVersion guards the race with the send step, which records its attempt first.
        var transfer = await InKindTransferChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = InKindTransferChange.ActorId(currentUser);
        InKindTransferChange.Guard(() => transfer.Cancel(actor, time.GetUtcNow(), request.Note));
        await InKindTransferChange.SaveAsync(db, cancellationToken);
    }
}

public class SetAsideInKindTransferCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<SetAsideInKindTransferCommand>
{
    public async Task Handle(SetAsideInKindTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await InKindTransferChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = InKindTransferChange.ActorId(currentUser);
        InKindTransferChange.Guard(() => transfer.SetAside(actor, time.GetUtcNow(), request.Note!));
        await InKindTransferChange.SaveAsync(db, cancellationToken);
    }
}

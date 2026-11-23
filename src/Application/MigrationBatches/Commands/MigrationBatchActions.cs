using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R17: submit a valid <c>Draft</c> for a second person to post.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class SubmitMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Migration batches R18: send a <c>Submitted</c> batch back to <c>Draft</c> with a note.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class ReturnMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Migration batches R19: end a batch without posting it; its funding lines go back to suspense.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class CancelMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class SubmitMigrationBatchCommandValidator : AbstractValidator<SubmitMigrationBatchCommand>
{
    public SubmitMigrationBatchCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId).Must(MigrationBatchBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class ReturnMigrationBatchCommandValidator : AbstractValidator<ReturnMigrationBatchCommand>
{
    public ReturnMigrationBatchCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(MigrationBatchActionRules.IsNote).WithMessage(MigrationBatchActionRules.NoteMessage);
        RuleFor(command => command.TenantId).Must(MigrationBatchBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class CancelMigrationBatchCommandValidator : AbstractValidator<CancelMigrationBatchCommand>
{
    public CancelMigrationBatchCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(MigrationBatchActionRules.IsNote).WithMessage(MigrationBatchActionRules.NoteMessage);
        RuleFor(command => command.TenantId).Must(MigrationBatchBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class SubmitMigrationBatchCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time) : IRequestHandler<SubmitMigrationBatchCommand>
{
    public async Task Handle(SubmitMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken, withBody: true);
        var actor = MigrationBatchActionRules.ActorId(currentUser);
        if (batch.Status != MigrationBatchStatus.Draft)
        {
            throw MigrationBatchRules.StatusError(batch.StatusMessage("submitted"));
        }

        var state = await MigrationBatchValidator.ValidateAsync(db, catalog, batch, cancellationToken);
        if (!state.Result.IsValid)
        {
            throw new MigrationBatchNotValidException(state.Result.Issues.Count);
        }

        MigrationBatchRules.Guard(() => batch.Submit(actor, time.GetUtcNow()));
        await MigrationBatchRules.SaveAsync(db, cancellationToken);
    }
}

public class ReturnMigrationBatchCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<ReturnMigrationBatchCommand>
{
    public async Task Handle(ReturnMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = MigrationBatchActionRules.ActorId(currentUser);
        MigrationBatchRules.Guard(() => batch.Return(actor, time.GetUtcNow(), request.Note));
        await MigrationBatchRules.SaveAsync(db, cancellationToken);
    }
}

public class CancelMigrationBatchCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<CancelMigrationBatchCommand>
{
    public async Task Handle(CancelMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = MigrationBatchActionRules.ActorId(currentUser);
        var now = time.GetUtcNow();
        MigrationBatchRules.Guard(() => batch.Cancel(actor, now, request.Note));

        // R19: every funding line goes back to suspense in the same database transaction, as an undo with the note.
        var lines = await db.BankStatementLines
            .Where(line => line.MigrationBatchId == batch.Id && line.Status == BankStatementLineStatus.Matched)
            .ToListAsync(cancellationToken);
        foreach (var line in lines)
        {
            line.Unmatch(request.Note!, actor, now);
        }

        await MigrationBatchRules.SaveAsync(db, cancellationToken);
    }
}

/// <summary>What the action commands share: the person acting and the note of return and cancel (R18, R19).</summary>
internal static class MigrationBatchActionRules
{
    public static readonly string NoteMessage = $"Note must be 1 to {MigrationBatch.NoteMaxLength} characters.";

    public static bool IsNote(string? note) => note?.Trim().Length is > 0 and <= MigrationBatch.NoteMaxLength;

    public static int ActorId(ICurrentUser currentUser) =>
        currentUser.Person?.Id ?? throw new ForbiddenAccessException();
}

using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>What every statement line action shares (deposit-matching R24, R25, R31).</summary>
internal static class StatementLineActions
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsRequiredNote(string? note) => note?.Trim().Length is >= 1 and <= BankStatementLine.NoteMaxLength;

    public static bool IsOptionalNote(string? note) => (note?.Trim().Length ?? 0) <= BankStatementLine.NoteMaxLength;

    /// <summary>R24: a stale <c>rowVersion</c> is 409 <c>stale_row_version</c>.</summary>
    public static void CheckRowVersion(BankStatementLine line, string? rowVersion)
    {
        var expected = Convert.ToBase64String(line.RowVersion ?? []);
        if (!string.Equals(expected, rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }
    }

    /// <summary>R24: a line not in a "From" status of its action, or a debit line, is 400 at <c>Status</c>.</summary>
    public static void Guard(Action check)
    {
        try
        {
            check();
        }
        catch (StatementLineStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }
    }

    /// <summary>The person who acts (R31).</summary>
    public static int ActorId(ICurrentUser currentUser) =>
        currentUser.Person?.Id ?? throw new ForbiddenAccessException();

    /// <summary>Saves; another writer's change of the line since it was read is 409 (R24).</summary>
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

    /// <summary>Applies a status change that writes nothing to the ledger (R27–R29).</summary>
    public static async Task ChangeAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        Guid id,
        string? rowVersion,
        Action<BankStatementLine, int, DateTimeOffset> change,
        CancellationToken cancellationToken)
    {
        var line = await StatementLineReader.FindAsync(db, currentUser, id, cancellationToken);
        CheckRowVersion(line, rowVersion);
        var actor = ActorId(currentUser);
        Guard(() => change(line, actor, time.GetUtcNow()));
        await SaveAsync(db, cancellationToken);
    }
}

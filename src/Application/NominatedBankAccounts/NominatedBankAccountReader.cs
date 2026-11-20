using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.NominatedBankAccounts;

/// <summary>Scope, reads and the shared action steps of nominated bank accounts (withdrawals R1, R8, R13, R16).</summary>
internal static class NominatedBankAccountReader
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsRequiredNote(string? note) =>
        note?.Trim().Length is >= 1 and <= NominatedBankAccount.NoteMaxLength;

    /// <summary>R1: the current tenant's Customers the caller may see: all for a TenantAdmin, the assigned ones for an
    /// Adviser, themselves for a Customer.</summary>
    public static IQueryable<User> Customers(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var tenantId = currentUser.TenantId ?? 0;
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        return db.Users.Where(customer =>
            customer.TenantId == tenantId
            && customer.Role == UserRole.Customer
            && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            && (selfCustomerId == null || customer.Id == selfCustomerId));
    }

    /// <summary>R1: the nominated bank accounts of <see cref="Customers"/>.</summary>
    public static IQueryable<NominatedBankAccount> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var customers = Customers(db, currentUser);
        return db.NominatedBankAccounts.Where(row => customers.Any(customer => customer.Id == row.CustomerId));
    }

    /// <summary>The row in scope, tracked; outside scope, another tenant's or missing is 404.</summary>
    public static async Task<NominatedBankAccount> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        CancellationToken cancellationToken) =>
        await Scope(db, currentUser).SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken)
        ?? throw new NotFoundException("Nominated bank account", id);

    /// <summary>R8: a stale <c>rowVersion</c> is 409 <c>stale_row_version</c>.</summary>
    public static void CheckRowVersion(NominatedBankAccount row, string? rowVersion)
    {
        if (!string.Equals(Convert.ToBase64String(row.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }
    }

    /// <summary>R8, R9: a wrong status or the adder reviewing is 400 at <c>Status</c>.</summary>
    public static void Guard(Action change)
    {
        try
        {
            change();
        }
        catch (NominatedBankAccountStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }
    }

    public static int ActorId(ICurrentUser currentUser) =>
        currentUser.Person?.Id ?? throw new ForbiddenAccessException();

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

    public static async Task<List<NominatedBankAccountDto>> ToDtosAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IReadOnlyList<NominatedBankAccount> rows,
        CancellationToken cancellationToken)
    {
        var userIds = rows.SelectMany(row => new[] { row.CustomerId, row.AddedByUserId, row.ReviewedByUserId ?? 0, row.RemovedByUserId ?? 0 })
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);

        // R16: a Customer does not read why a row was rejected or removed.
        var customer = currentUser.Role == UserRole.Customer;
        return rows.Select(row => new NominatedBankAccountDto
        {
            Id = row.PublicId,
            CustomerId = users[row.CustomerId].Id,
            Currency = row.Currency.Trim(),
            BankName = row.BankName,
            AccountName = row.AccountName,
            AccountNumber = row.AccountNumber,
            Status = row.Status.ToString(),
            Added = new NominatedBankAccountEventDto { By = users[row.AddedByUserId], At = row.AddedAt },
            Review = row.ReviewedByUserId is int reviewer
                ? new NominatedBankAccountEventDto
                {
                    By = users[reviewer],
                    At = row.ReviewedAt!.Value,
                    Note = customer && row.Status == NominatedBankAccountStatus.Rejected ? null : row.ReviewNote
                }
                : null,
            Removal = row.RemovedByUserId is int remover
                ? new NominatedBankAccountEventDto
                {
                    By = users[remover],
                    At = row.RemovedAt!.Value,
                    Note = customer ? null : row.RemovedNote
                }
                : null,
            RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
        }).ToList();
    }
}

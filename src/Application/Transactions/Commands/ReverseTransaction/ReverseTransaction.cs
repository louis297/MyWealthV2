using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions.Commands.ReverseTransaction;

[Authorize(Policy = Policies.TransactionsCreate)]
public class ReverseTransactionCommand : IRequest<IdempotentResult>
{
    public Guid Id { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class ReverseTransactionCommandValidator : AbstractValidator<ReverseTransactionCommand>
{
    public ReverseTransactionCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }
}

public class ReverseTransactionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<ReverseTransactionCommand, IdempotentResult>
{
    public async Task<IdempotentResult> Handle(ReverseTransactionCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(request.Id, cancellationToken);
        var key = Guid.Parse(request.IdempotencyKey!);
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? $"/transactions/{request.Id}/reverse",
            new { });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await ReverseAsync(request, loaded, key, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            var replay = await FindRecordAsync(loaded.Transaction.TenantId, key, cancellationToken);
            if (replay is not null)
            {
                return ReplayOrConflict(replay, hash);
            }

            throw;
        }

        return result!;
    }

    private async Task<IdempotentResult> ReverseAsync(
        ReverseTransactionCommand request,
        LoadedTransaction loaded,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(loaded.Transaction.TenantId, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        if (loaded.Account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot reverse a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        if (loaded.Customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                loaded.Customer.PublicId,
                "User is disabled",
                "Cannot reverse a transaction while the customer is disabled. Enable the customer first.");
        }

        if (loaded.Transaction.Type == TransactionType.Reversal
            || await db.Transactions.AnyAsync(
                row => row.OriginalTransactionId == loaded.Transaction.Id, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "This transaction has already been reversed.")
            ]);
        }

        await db.LockAccountAsync(loaded.Account.Id, cancellationToken);
        var currentSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);
        Domain.Entities.Transaction reversal;
        try
        {
            reversal = loaded.Transaction.Reverse(time.GetUtcNow(), currentSum);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, exception.Message)
            ]);
        }

        db.Transactions.Add(reversal);
        var body = JsonSerializer.Serialize(new { id = reversal.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(
            loaded.Transaction.TenantId,
            key,
            request.RequestMethod ?? "POST",
            request.RequestPath ?? $"/transactions/{request.Id}/reverse",
            hash,
            201,
            body,
            time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);

        var postedSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);
        if (postedSum < 0)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        return new IdempotentResult(201, body);
    }

    private async Task<LoadedTransaction> LoadAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var query =
            from transaction in db.Transactions
            join account in db.Accounts on transaction.AccountId equals account.Id
            join customer in db.Users on account.CustomerId equals customer.Id
            where transaction.PublicId == publicId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { transaction, account, customer };

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.transaction.TenantId == currentUser.TenantId);
        }

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Transaction", publicId);
        await db.LoadCashLegAsync(row.transaction, cancellationToken);
        return new LoadedTransaction(row.transaction, row.account, row.customer);
    }

    private Task<IdempotencyRecord?> FindRecordAsync(int tenantId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(
            record => record.TenantId == tenantId && record.Key == key,
            cancellationToken);

    private static IdempotentResult ReplayOrConflict(IdempotencyRecord existing, string hash)
    {
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        return new IdempotentResult(existing.ResponseStatus, existing.ResponseBody);
    }

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));

    private sealed record LoadedTransaction(
        Domain.Entities.Transaction Transaction,
        Account Account,
        User Customer);
}

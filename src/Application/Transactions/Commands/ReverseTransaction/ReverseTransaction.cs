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

        if (loaded.LegAccounts.Any(leg => leg.Account.Status == AccountStatus.Closed))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot reverse a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        var disabled = loaded.LegAccounts.FirstOrDefault(leg => leg.Customer.Status == UserStatus.Disabled);
        if (disabled is not null)
        {
            throw new TargetDisabledException(
                "user",
                disabled.Customer.PublicId,
                "User is disabled",
                "Cannot reverse a transaction while the customer is disabled. Enable the customer first.");
        }

        foreach (var leg in loaded.LegAccounts)
        {
            await db.LockAccountAsync(leg.Account.Id, cancellationToken);
        }

        var header = await db.Transactions.AsNoTracking()
            .Where(row => row.Id == loaded.Transaction.Id)
            .Select(row => new { row.Type, row.ReversedByTransactionId })
            .SingleAsync(cancellationToken);
        foreach (var leg in loaded.LegAccounts)
        {
            await PortfolioTradeLock.RejectIfAssignedAsync(db, leg.Account.Id, header.Type, cancellationToken);
        }

        if (header.Type == TransactionType.Reversal || header.ReversedByTransactionId is not null)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "This transaction has already been reversed.")
            ]);
        }
        var currentSums = new Dictionary<int, decimal>();
        foreach (var leg in loaded.LegAccounts)
        {
            currentSums[leg.Account.Id] = await TransactionCash.SumAsync(db, leg.Account.Id, cancellationToken);
        }

        var currentQuantities = await CurrentQuantitiesAsync(loaded, cancellationToken);
        Domain.Entities.Transaction reversal;
        try
        {
            reversal = loaded.Transaction.Reverse(time.GetUtcNow(), currentSums, currentQuantities);
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

        if (reversal.SecurityLegs.Count > 0)
        {
            try
            {
                foreach (var account in reversal.SecurityLegs.GroupBy(leg => leg.AccountId))
                {
                    await HoldingProjection.SyncAsync(
                        db,
                        loaded.Transaction.TenantId,
                        account.Key,
                        account.Select(leg => leg.InstrumentId),
                        cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DomainException exception)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, exception.Message)
                ]);
            }
        }

        foreach (var leg in loaded.LegAccounts)
        {
            var postedSum = await TransactionCash.SumAsync(db, leg.Account.Id, cancellationToken);
            if (postedSum < 0)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
                ]);
            }
        }

        return new IdempotentResult(201, body);
    }

    private async Task<LoadedTransaction> LoadAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var query = db.Transactions.Where(row => row.PublicId == publicId);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.TenantId == currentUser.TenantId);
        }

        var transaction = await query.SingleOrDefaultAsync(cancellationToken)
                          ?? throw new NotFoundException("Transaction", publicId);

        var legAccounts = await (
            from legAccount in TransactionLegAccounts.Query(db)
            join account in db.Accounts on legAccount.AccountId equals account.Id
            join customer in db.Users on account.CustomerId equals customer.Id
            where legAccount.TransactionId == transaction.Id
            orderby account.Id
            select new LegAccount(account, customer)).ToListAsync(cancellationToken);

        // Write authority covers every leg account (leg-accounts R6).
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        if (legAccounts.Count == 0
            || legAccounts.Any(leg => leg.Account.TenantId != transaction.TenantId)
            || (assignedAdviserId is not null && legAccounts.Any(leg => leg.Customer.AdviserId != assignedAdviserId)))
        {
            throw new NotFoundException("Transaction", publicId);
        }

        await db.LoadCashLegsAsync(transaction, cancellationToken);
        await db.LoadSecurityLegsAsync(transaction, cancellationToken);
        return new LoadedTransaction(transaction, legAccounts);
    }

    private async Task<IReadOnlyDictionary<(int AccountId, int InstrumentId), decimal>?> CurrentQuantitiesAsync(
        LoadedTransaction loaded,
        CancellationToken cancellationToken)
    {
        if (loaded.Transaction.SecurityLegs.Count == 0)
        {
            return null;
        }

        var quantities = new Dictionary<(int AccountId, int InstrumentId), decimal>();
        foreach (var leg in loaded.Transaction.SecurityLegs)
        {
            var holding = await db.Holdings.AsNoTracking().SingleOrDefaultAsync(
                row => row.AccountId == leg.AccountId && row.InstrumentId == leg.InstrumentId,
                cancellationToken);
            quantities[(leg.AccountId, leg.InstrumentId)] = holding?.Quantity ?? 0m;
        }

        return quantities;
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
        IReadOnlyList<LegAccount> LegAccounts);

    private sealed record LegAccount(Account Account, User Customer);
}

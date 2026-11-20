using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
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
    TimeProvider time,
    ICurrencyCatalog currencies)
    : IRequestHandler<ReverseTransactionCommand, IdempotentResult>
{
    public async Task<IdempotentResult> Handle(ReverseTransactionCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(request.Id, cancellationToken);

        // Adjustments R17: after the Transaction is found and visible (404 first), before every other reverse rule.
        if (loaded.Transaction.Type == TransactionType.Adjustment
            && (currentUser.Role is not { } role || !RolePermissions.Has(role, Policies.TransactionsAdjust)))
        {
            throw new ForbiddenAccessException();
        }
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

        // Transaction source R13, R14: every leg account has the same custody mode (custody-modes R9), so the
        // first one decides. Runs before the portfolio trade lock and the already-reversed check.
        if (!CustodyRules.AllowsHandReverse(loaded.LegAccounts[0].Account.CustodyMode, loaded.Transaction.Source.Kind))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, CustodyRules.HandReverseMessage)
            ]);
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
        var hasLaterSecurityActivity = await HasLaterSecurityActivityAsync(loaded.Transaction, cancellationToken);
        Domain.Entities.Transaction reversal;
        try
        {
            reversal = loaded.Transaction.Reverse(
                TransactionSource.Manual, time.GetUtcNow(), currentSums, currentQuantities, hasLaterSecurityActivity);
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

            await AvailableCashGuard.EnsureAsync(db, currencies, leg.Account, postedSum, cancellationToken);
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

        // The FX, adjustment and fee details are copied to the Reversal (FX conversions R17, adjustments R18,
        // trade-fees R19).
        var transaction = await query
                              .Include(row => row.FxDetail)
                              .Include(row => row.AdjustmentDetail)
                              .Include(row => row.FeeDetails)
                              .SingleOrDefaultAsync(cancellationToken)
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

    /// <summary>
    /// True when a live security leg written after <paramref name="original"/> exists on one of its
    /// <c>(Account, Instrument)</c> pairs. Live: its Transaction is not a Reversal and has not been reversed
    /// (fixes/ledger-write-guards.md Decision 1). Runs after the leg Accounts are locked.
    /// </summary>
    private async Task<bool> HasLaterSecurityActivityAsync(
        Domain.Entities.Transaction original,
        CancellationToken cancellationToken)
    {
        if (original.SecurityLegs.Count == 0)
        {
            return false;
        }

        var holdings = original.SecurityLegs
            .Select(leg => (leg.AccountId, leg.InstrumentId))
            .ToHashSet();
        var accountIds = holdings.Select(holding => holding.AccountId).Distinct().ToList();
        var instrumentIds = holdings.Select(holding => holding.InstrumentId).Distinct().ToList();

        var later = await (
            from leg in db.TransactionSecurityLegs.AsNoTracking()
            join transaction in db.Transactions.AsNoTracking() on leg.TransactionId equals transaction.Id
            where accountIds.Contains(leg.AccountId)
                  && instrumentIds.Contains(leg.InstrumentId)
                  && transaction.Id > original.Id
                  && transaction.Type != TransactionType.Reversal
                  && transaction.ReversedByTransactionId == null
            select new { leg.AccountId, leg.InstrumentId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return later.Any(row => holdings.Contains((row.AccountId, row.InstrumentId)));
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

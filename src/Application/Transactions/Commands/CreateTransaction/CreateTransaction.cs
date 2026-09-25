using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions.Commands.CreateTransaction;

[Authorize(Policy = Policies.TransactionsCreate)]
public class CreateTransactionCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid AccountId { get; init; }

    public string? Type { get; init; }

    public decimal Amount { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    private static readonly string[] AllowedTypes = ["TransferIn", "TransferOut", "Interest", "CloseOut", "Opening"];

    public CreateTransactionCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.Type)
            .Must(type => type is not null && AllowedTypes.Contains(type))
            .WithMessage("Type must be TransferIn, TransferOut, Interest, CloseOut, or Opening.");
        RuleFor(command => command.BookedAt).NotNull();
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= 200)
            .WithMessage("Memo must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= 100)
            .WithMessage("Reference must be at most 100 characters.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class CreateTransactionCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CreateTransactionCommand, IdempotentResult>
{
    public async Task<IdempotentResult> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(request.TenantId, cancellationToken);
        var key = Guid.Parse(request.IdempotencyKey!);
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? "/transactions",
            new
            {
                accountId = request.AccountId,
                type = request.Type,
                amount = request.Amount,
                bookedAt = request.BookedAt?.ToString("o"),
                memo = request.Memo,
                reference = request.Reference,
                currency = request.Currency,
                tenantId = request.TenantId
            });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await CreateAsync(request, tenant, key, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            var replay = await FindReplayAsync(tenant.Id, key, hash, cancellationToken);
            if (replay is not null)
            {
                return replay;
            }

            throw;
        }

        return result!;
    }

    private async Task<IdempotentResult> CreateAsync(
        CreateTransactionCommand request,
        Tenant tenant,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a transaction while the tenant is disabled. Enable the tenant first.");
        }

        var loaded = await LoadAccountAsync(request.AccountId, tenant.Id, cancellationToken);
        await db.LockAccountAsync(loaded.Account.Id, cancellationToken);

        if (loaded.Account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        if (loaded.Customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                loaded.Customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        var currency = loaded.Account.Currency;
        if (request.Currency is not null)
        {
            var requested = Money.Create(0m, request.Currency).Currency;
            var accountCurrency = Money.Create(0m, currency).Currency;
            if (!string.Equals(requested, accountCurrency, StringComparison.Ordinal))
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(CreateTransactionCommand.Currency), "Currency must match the account currency.")
                ]);
            }

            currency = accountCurrency;
        }

        var currentSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);
        var existingCount = await TransactionCash.CountAsync(db, loaded.Account.Id, cancellationToken);
        var type = Enum.Parse<TransactionType>(request.Type!);
        Domain.Entities.Transaction transaction;
        try
        {
            transaction = Domain.Entities.Transaction.Post(
                tenant.Id,
                loaded.Account.Id,
                type,
                request.Amount,
                currency,
                request.BookedAt!.Value,
                request.Memo,
                request.Reference,
                currentSum,
                existingCount);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, exception.Message)
            ]);
        }

        db.Transactions.Add(transaction);
        var body = JsonSerializer.Serialize(new { id = transaction.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(
            tenant.Id,
            key,
            request.RequestMethod ?? "POST",
            request.RequestPath ?? "/transactions",
            hash,
            201,
            body,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        var postedSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);
        if (postedSum < 0 || (type == TransactionType.CloseOut && postedSum != 0))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        return new IdempotentResult(201, body);
    }

    private async Task<IdempotentResult?> FindReplayAsync(
        int tenantId,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenantId, key, cancellationToken);
        return existing is null ? null : ReplayOrConflict(existing, hash);
    }

    private static IdempotentResult ReplayOrConflict(IdempotencyRecord existing, string hash)
    {
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        return new IdempotentResult(existing.ResponseStatus, existing.ResponseBody);
    }

    private Task<IdempotencyRecord?> FindRecordAsync(int tenantId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(
            record => record.TenantId == tenantId && record.Key == key,
            cancellationToken);

    private async Task<Tenant> ResolveTenantAsync(Guid? tenantPublicId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.SingleOrDefaultAsync(
                       row => row.PublicId == tenantPublicId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", tenantPublicId!);
        }

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }

    private async Task<(Account Account, User Customer)> LoadAccountAsync(
        Guid accountPublicId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var row = await (
            from account in db.Accounts
            join customer in db.Users on account.CustomerId equals customer.Id
            where account.PublicId == accountPublicId
                  && account.TenantId == tenantId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { account, customer }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Account", accountPublicId);
        return (row.account, row.customer);
    }

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));
}

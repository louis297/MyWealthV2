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

namespace MyWealthV2.Application.Transactions.Commands.CreateInternalTransfer;

/// <summary>Internal transfers §7: cash from one Account to another Account in the same tenant.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class CreateInternalTransferCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? Amount { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    /// <summary>Not part of the body (R3). Declared only so that a request that sends it is rejected.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public Guid? AccountId { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public string? Currency { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Cash { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Security { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class CreateInternalTransferCommandValidator : AbstractValidator<CreateInternalTransferCommand>
{
    public CreateInternalTransferCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.FromAccountId).NotEmpty();
        RuleFor(command => command.ToAccountId).NotEmpty();
        RuleFor(command => command.ToAccountId)
            .Must((command, to) => to != command.FromAccountId)
            .When(command => command.FromAccountId is not null && command.ToAccountId is not null)
            .WithMessage("The from and to accounts must be different.");
        RuleFor(command => command.Amount)
            .NotNull()
            .GreaterThan(0)
            .WithMessage("Amount must be greater than 0.");
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

        RuleFor(command => command.Type).Null().WithMessage("Type cannot be set on a transfer.");
        RuleFor(command => command.AccountId).Null().WithMessage("AccountId cannot be set on a transfer. Use fromAccountId and toAccountId.");
        RuleFor(command => command.Currency).Null().WithMessage("Currency cannot be set on a transfer.");
        RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a transfer.");
        RuleFor(command => command.Security).Null().WithMessage("Security cannot be set on a transfer.");

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

public class CreateInternalTransferCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CreateInternalTransferCommand, IdempotentResult>
{
    private const string DefaultPath = "/transactions/transfer";

    public async Task<IdempotentResult> Handle(CreateInternalTransferCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(request.TenantId, cancellationToken);
        var key = Guid.Parse(request.IdempotencyKey!);
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            new
            {
                fromAccountId = request.FromAccountId,
                toAccountId = request.ToAccountId,
                amount = request.Amount,
                bookedAt = request.BookedAt?.ToString("o"),
                memo = request.Memo,
                reference = request.Reference,
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
            var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
            if (existing is not null)
            {
                return ReplayOrConflict(existing, hash);
            }

            throw;
        }

        return result!;
    }

    private async Task<IdempotentResult> CreateAsync(
        CreateInternalTransferCommand request,
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

        // R6: both Accounts resolve in scope, or the whole command is 404.
        var from = await LoadAccountAsync(request.FromAccountId!.Value, tenant.Id, cancellationToken);
        var to = await LoadAccountAsync(request.ToAccountId!.Value, tenant.Id, cancellationToken);

        // R10: lock both Account rows in ascending Accounts.Id order.
        foreach (var accountId in new[] { from.Account.Id, to.Account.Id }.Order())
        {
            await db.LockAccountAsync(accountId, cancellationToken);
        }

        foreach (var side in new[] { from, to })
        {
            if (side.Account.Status == AccountStatus.Closed)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
                ]);
            }
        }

        foreach (var side in new[] { from, to })
        {
            if (side.Customer.Status == UserStatus.Disabled)
            {
                throw new TargetDisabledException(
                    "user",
                    side.Customer.PublicId,
                    "User is disabled",
                    "Cannot create a transaction while the customer is disabled. Enable the customer first.");
            }
        }

        var fromCurrency = Money.Create(0m, from.Account.Currency).Currency;
        if (!string.Equals(fromCurrency, Money.Create(0m, to.Account.Currency).Currency, StringComparison.Ordinal))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(CreateInternalTransferCommand.ToAccountId),
                    "The from and to accounts must have the same currency.")
            ]);
        }

        var fromSum = await TransactionCash.SumAsync(db, from.Account.Id, cancellationToken);
        Domain.Entities.Transaction transaction;
        try
        {
            transaction = Domain.Entities.Transaction.PostInternalTransfer(
                tenant.Id,
                from.Account.Id,
                from.Account.Currency,
                to.Account.Id,
                to.Account.Currency,
                Money.Create(request.Amount!.Value, fromCurrency),
                request.BookedAt!.Value,
                request.Memo,
                request.Reference,
                fromSum);
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
            request.RequestPath ?? DefaultPath,
            hash,
            201,
            body,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        if (await TransactionCash.SumAsync(db, from.Account.Id, cancellationToken) < 0)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        return new IdempotentResult(201, body);
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

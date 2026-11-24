using System.Text.Json;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>In-kind transfers R21–R26: request a transfer of securities in or out of a Custodial account.</summary>
[Authorize(Policy = Policies.InKindTransfersRequest)]
public sealed class RequestInKindTransferCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public string? Direction { get; init; }

    public Guid? InstrumentId { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Cost { get; init; }

    public DateOnly? AcquiredOn { get; init; }

    public bool? ChangesBeneficialOwner { get; init; }

    public CounterpartyBody? Counterparty { get; init; }

    public string? Memo { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public string? BookedAt { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public string? Status { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public Guid? Id { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    internal TransferDirection ParsedDirection => Enum.Parse<TransferDirection>(Direction!, ignoreCase: false);
}

/// <summary>R21: the other broker, custodian or registry and the holder's account there.</summary>
public sealed class CounterpartyBody
{
    public string? Name { get; init; }

    public string? Account { get; init; }

    public string? HolderName { get; init; }
}

public class RequestInKindTransferCommandValidator : AbstractValidator<RequestInKindTransferCommand>
{
    public const string CostOnlyInMessage = "Cost is given only for a transfer in.";

    public RequestInKindTransferCommandValidator(TimeProvider time)
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.InstrumentId).NotEmpty();
        RuleFor(command => command.Direction)
            .Must(value => value is "In" or "Out")
            .WithMessage("Direction must be In or Out.");
        RuleFor(command => command.Quantity)
            .Must(value => value is > 0 && decimal.Round(value.Value, 8) == value.Value)
            .WithMessage("Quantity must be greater than 0, with at most 8 decimal places.");
        RuleFor(command => command.ChangesBeneficialOwner).NotNull().WithMessage("Say whether the owner changes.");
        RuleFor(command => command.Counterparty).NotNull().WithMessage("Counterparty is required.");
        When(command => command.Counterparty is not null, () =>
        {
            RuleFor(command => command.Counterparty!.Name)
                .Must(value => Length(value) is >= 1 and <= InKindTransfer.CounterpartyNameMaxLength)
                .WithMessage($"Counterparty name must be 1 to {InKindTransfer.CounterpartyNameMaxLength} characters.");
            RuleFor(command => command.Counterparty!.Account)
                .Must(ClientMoneyAccount.IsValidAccountNumber)
                .WithMessage(
                    $"Counterparty account must be 1 to {ClientMoneyAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
            RuleFor(command => command.Counterparty!.HolderName)
                .Must(value => Length(value) is >= 1 and <= InKindTransfer.CounterpartyHolderNameMaxLength)
                .WithMessage($"Name on the account must be 1 to {InKindTransfer.CounterpartyHolderNameMaxLength} characters.");
        });
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || Length(memo) is >= 1 and <= InKindTransfer.MemoMaxLength)
            .WithMessage($"Memo must be 1 to {InKindTransfer.MemoMaxLength} characters.");
        When(command => command.Direction == "In", () =>
        {
            RuleFor(command => command.Cost).NotNull().WithMessage("Cost is required for a transfer in.");
            RuleFor(command => command.Cost)
                .Must(cost => cost >= 0 && decimal.Round(cost.Value, 4) == cost.Value)
                .When(command => command.Cost is not null)
                .WithMessage("Cost must be 0 or more, with at most 4 decimal places.");
            RuleFor(command => command.AcquiredOn)
                .Must(date => date <= DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
                .When(command => command.AcquiredOn is not null)
                .WithMessage("Acquired on cannot be after today.");
        });
        When(command => command.Direction == "Out", () =>
        {
            RuleFor(command => command.Cost).Null().WithMessage(CostOnlyInMessage);
            RuleFor(command => command.AcquiredOn).Null().WithMessage(CostOnlyInMessage);
        });
        RuleFor(command => command.BookedAt).Null().WithMessage("BookedAt cannot be set.");
        RuleFor(command => command.Status).Null().WithMessage("Status cannot be set.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Id).Null().WithMessage("Id cannot be set.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }

    private static int Length(string? value) => value?.Trim().Length ?? 0;
}

public class RequestInKindTransferCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICustodianConnections connections,
    TimeProvider time) : IRequestHandler<RequestInKindTransferCommand, IdempotentResult>
{
    private const string DefaultPath = "/in-kind-transfers";

    public async Task<IdempotentResult> Handle(RequestInKindTransferCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var key = Guid.Parse(request.IdempotencyKey!);
        var method = request.RequestMethod ?? "POST";
        var path = request.RequestPath ?? DefaultPath;
        var hash = TransactionRequestHash.Compute(method, path, new
        {
            accountId = request.AccountId,
            direction = request.Direction,
            instrumentId = request.InstrumentId,
            quantity = request.Quantity,
            cost = request.Cost,
            acquiredOn = request.AcquiredOn,
            changesBeneficialOwner = request.ChangesBeneficialOwner,
            counterparty = new { request.Counterparty?.Name, request.Counterparty?.Account, request.Counterparty?.HolderName },
            memo = request.Memo
        });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await RequestAsync(request, tenantId, key, method, path, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            db.ClearTrackedChanges();
            var existing = await FindRecordAsync(tenantId, key, cancellationToken);
            if (existing is not null)
            {
                return ReplayOrConflict(existing, hash);
            }

            throw;
        }

        return result!;
    }

    private async Task<IdempotentResult> RequestAsync(
        RequestInKindTransferCommand request, int tenantId, Guid key, string method, string path, string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenantId, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        // R22: the Account is in scope, or the whole command is 404.
        var accountId = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                            .Where(row => row.PublicId == request.AccountId)
                            .Select(row => (int?)row.Id)
                            .SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Account", request.AccountId!.Value);

        // R26: the transferable quantity is checked under the Account lock, as every posting is.
        await db.LockAccountAsync(accountId, cancellationToken);
        var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == accountId, cancellationToken);
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw InKindTransferRules.Error(nameof(RequestInKindTransferCommand.AccountId), InKindTransferRules.NotCustodialMessage);
        }

        await InKindTransferRules.EnsureAccountTakesTransfersAsync(db, account, cancellationToken);

        // R24: on an assigned account only its rebalance changes holdings.
        if (await InKindTransferRules.IsAssignedAsync(db, account.Id, cancellationToken))
        {
            throw InKindTransferRules.Error(nameof(RequestInKindTransferCommand.AccountId), InKindTransferRules.AssignedMessage);
        }

        // R23: an Instrument of the Tenant; a transfer in needs it active and quoted in the Account's currency.
        var direction = request.ParsedDirection;
        var instrument = await db.Instruments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PublicId == request.InstrumentId && row.TenantId == tenantId, cancellationToken)
            ?? throw InKindTransferRules.Error(nameof(RequestInKindTransferCommand.InstrumentId), InKindTransferRules.InstrumentNotFoundMessage);
        var quote = instrument.QuoteCurrency.Trim().ToUpperInvariant();
        if (direction == TransferDirection.In)
        {
            if (!instrument.IsActive)
            {
                throw InKindTransferRules.Error(nameof(RequestInKindTransferCommand.InstrumentId), InKindTransferRules.InstrumentDisabledMessage);
            }

            var accountCurrency = account.Currency.Trim().ToUpperInvariant();
            if (!string.Equals(quote, accountCurrency, StringComparison.Ordinal))
            {
                throw InKindTransferRules.Error(nameof(RequestInKindTransferCommand.InstrumentId),
                    InKindTransferRules.CurrencyMessage(instrument.Symbol, quote, accountCurrency));
            }
        }

        await InKindTransferRules.SafekeepingAccountAsync(db, connections, tenantId, quote, instrument.Symbol, cancellationToken);

        var transfer = InKindTransfer.Request(
            tenantId, account.Id, instrument.Id, direction, request.Quantity!.Value, quote,
            direction == TransferDirection.In ? request.Cost : null, request.AcquiredOn, request.ChangesBeneficialOwner!.Value,
            request.Counterparty!.Name!, request.Counterparty.Account!, request.Counterparty.HolderName!, request.Memo,
            currentUser.Person?.Id ?? throw new ForbiddenAccessException(), time.GetUtcNow());
        db.InKindTransfers.Add(transfer);
        var body = JsonSerializer.Serialize(new { id = transfer.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, key, method, path, hash, 201, body, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return new IdempotentResult(201, body);
    }

    private static IdempotentResult ReplayOrConflict(IdempotencyRecord existing, string hash) =>
        string.Equals(existing.RequestHash, hash, StringComparison.Ordinal)
            ? new IdempotentResult(existing.ResponseStatus, existing.ResponseBody)
            : throw new IdempotencyConflictException();

    private Task<IdempotencyRecord?> FindRecordAsync(int tenantId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(record => record.TenantId == tenantId && record.Key == key, cancellationToken);

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));
}

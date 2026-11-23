using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Orders.Commands;

/// <summary>Orders R6–R13: place an order and reserve the most it can cost (a buy) or its quantity (a sell).</summary>
[Authorize(Policy = Policies.OrdersPlace)]
public sealed class PlaceOrderCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public Guid? InstrumentId { get; init; }

    public string? Side { get; init; }

    public decimal? Quantity { get; init; }

    public string? Memo { get; init; }

    /// <summary>May be sent, and must then be <c>Market</c> (R6).</summary>
    public string? Type { get; init; }

    /// <summary>May be sent, and must then be <c>Day</c> (R6).</summary>
    public string? TimeInForce { get; init; }

    /// <summary>Refused when present (R6): the platform sets the price limit.</summary>
    public JsonElement? Price { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? PriceLimit { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Limit { get; init; }

    /// <summary>Refused when present (R6): an order is by quantity.</summary>
    public JsonElement? Amount { get; init; }

    /// <summary>Refused when present (R6): a fill is booked when its report is processed.</summary>
    public JsonElement? BookedAt { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Status { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Id { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

/// <summary>The body rules place and preview share (R6).</summary>
internal static class OrderBodyRules
{
    public const string SideMessage = "Side must be Buy or Sell.";
    public const string QuantityMessage = "Quantity must be greater than 0 with at most 8 decimal places.";
    public const string MemoMessage = "Memo must be 1 to 200 characters.";

    public static bool IsSide(string? side) =>
        side is not null && Enum.GetNames<OrderSide>().Contains(side.Trim(), StringComparer.OrdinalIgnoreCase);

    public static OrderSide Side(string side) => Enum.Parse<OrderSide>(side.Trim(), ignoreCase: true);

    public static bool IsQuantity(decimal? quantity) =>
        quantity is { } value && value > 0m && decimal.Round(value, Order.QuantityDecimals) == value;

    public static bool IsOnly(string? value, string name) =>
        value is null || string.Equals(value.Trim(), name, StringComparison.OrdinalIgnoreCase);

    public static bool IsAbsent(JsonElement? value) => value is null || value.Value.ValueKind == JsonValueKind.Null;
}

public class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.InstrumentId).NotEmpty();
        RuleFor(command => command.Side).Must(OrderBodyRules.IsSide).WithMessage(OrderBodyRules.SideMessage);
        RuleFor(command => command.Quantity).Must(OrderBodyRules.IsQuantity).WithMessage(OrderBodyRules.QuantityMessage);
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= Order.MemoMaxLength)
            .WithMessage(OrderBodyRules.MemoMessage);
        RuleFor(command => command.Type)
            .Must(type => OrderBodyRules.IsOnly(type, nameof(OrderType.Market))).WithMessage("Type must be Market.");
        RuleFor(command => command.TimeInForce)
            .Must(timeInForce => OrderBodyRules.IsOnly(timeInForce, nameof(Domain.Enums.TimeInForce.Day)))
            .WithMessage("TimeInForce must be Day.");
        RuleFor(command => command.Price).Must(OrderBodyRules.IsAbsent).WithMessage("Price cannot be set. The platform sets the price limit.");
        RuleFor(command => command.PriceLimit).Must(OrderBodyRules.IsAbsent).WithMessage("PriceLimit cannot be set. The platform sets the price limit.");
        RuleFor(command => command.Limit).Must(OrderBodyRules.IsAbsent).WithMessage("Limit cannot be set. The platform sets the price limit.");
        RuleFor(command => command.Amount).Must(OrderBodyRules.IsAbsent).WithMessage("Amount cannot be set. An order is placed by quantity.");
        RuleFor(command => command.BookedAt).Must(OrderBodyRules.IsAbsent).WithMessage("BookedAt cannot be set.");
        RuleFor(command => command.Status).Must(OrderBodyRules.IsAbsent).WithMessage("Status cannot be set.");
        RuleFor(command => command.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Id).Must(OrderBodyRules.IsAbsent).WithMessage("Id cannot be set.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }
}

public class PlaceOrderCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    IBrokerConnections connections,
    IMarketData marketData,
    IInstrumentPriceStore prices,
    TimeProvider time) : IRequestHandler<PlaceOrderCommand, IdempotentResult>
{
    private const string DefaultPath = "/orders";

    public async Task<IdempotentResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var key = Guid.Parse(request.IdempotencyKey!);
        var method = request.RequestMethod ?? "POST";
        var path = request.RequestPath ?? DefaultPath;
        var hash = TransactionRequestHash.Compute(method, path, new
        {
            accountId = request.AccountId,
            instrumentId = request.InstrumentId,
            side = request.Side?.Trim().ToLowerInvariant(),
            quantity = request.Quantity,
            memo = request.Memo
        });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await PlaceAsync(request, tenantId, key, method, path, hash, token);
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

    private async Task<IdempotentResult> PlaceAsync(
        PlaceOrderCommand request,
        int tenantId,
        Guid key,
        string method,
        string path,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenantId, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        // R1, R7: the Account is in scope, or the whole command is 404.
        var accountId = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                            .Where(row => row.PublicId == request.AccountId)
                            .Select(row => (int?)row.Id)
                            .SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Account", request.AccountId!.Value);

        // R12, R13: the reservation is checked under the Account lock, as every posting is.
        await db.LockAccountAsync(accountId, cancellationToken);
        var now = time.GetUtcNow();
        var quote = await OrderChecks.RunAsync(
            new OrderCheckServices(db, currencies, connections, marketData, prices),
            accountId, request.InstrumentId!.Value, OrderBodyRules.Side(request.Side!), request.Quantity!.Value, now, cancellationToken);

        var order = Order.Place(
            tenantId, quote.Account.Id, quote.Instrument.Id, quote.Side, quote.Quantity, quote.Currency, quote.ReferencePrice,
            quote.ReferencePriceDate, quote.Fees, quote.Connection, request.Memo,
            currentUser.Person?.Id ?? throw new ForbiddenAccessException(), now,
            settlementAccount: await SettlementAccountAsync(tenantId, quote, cancellationToken));
        db.Orders.Add(order);
        var body = JsonSerializer.Serialize(new { id = order.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, key, method, path, hash, 201, body, now));
        await db.SaveChangesAsync(cancellationToken);
        return new IdempotentResult(201, body);
    }

    /// <summary>Trade-settlement R3: the Tenant's active client money account in the Account's currency, when the
    /// Instrument is quoted in that currency and the broker connection holds the broker's bank account for it.
    /// Otherwise none; having none never refuses an order.</summary>
    private async Task<ClientMoneyAccount?> SettlementAccountAsync(int tenantId, OrderQuote quote, CancellationToken cancellationToken)
    {
        var currency = quote.Currency.Trim().ToUpperInvariant();
        if (!string.Equals(currency, quote.Account.Currency.Trim(), StringComparison.OrdinalIgnoreCase)
            || connections.BankAccount(quote.Connection, currency) is null)
        {
            return null;
        }

        return await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.IsActive && row.Currency == currency)
            .SingleOrDefaultAsync(cancellationToken);
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

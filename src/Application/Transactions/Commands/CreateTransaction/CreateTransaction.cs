using MyWealthV2.Application.AccountClosures;
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

namespace MyWealthV2.Application.Transactions.Commands.CreateTransaction;

[Authorize(Policy = Policies.TransactionsCreate)]
public class CreateTransactionCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid AccountId { get; init; }

    public string? Type { get; init; }

    public decimal? Amount { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
    
    public List<OpeningHoldingSecurity>? Security { get; init; }

    public OpeningHoldingsCash? Cash { get; init; }

    public decimal? Ratio { get; init; }

    /// <summary>
    /// The brokerage the outside broker charged on an External <c>Buy</c> / <c>Sell</c>, in the instrument's quote
    /// currency (external-caller-numbers R6). Refused on a Custodial account, where the server computes it (trade-fees R14).
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// Account currency per 1 quote currency on an External <c>Buy</c> / <c>Sell</c> across currencies
    /// (external-caller-numbers R7). Refused on a Custodial account.
    /// </summary>
    public decimal? FxRate { get; init; }

    public sealed class OpeningHoldingSecurity
    {
        public Guid InstrumentId { get; init; }

        public decimal Quantity { get; init; }

        public decimal? Cost { get; init; }

        public decimal? Price { get; init; }

        public decimal? Ratio { get; init; }
    }

    public sealed class OpeningHoldingsCash
    {
        public decimal? Amount { get; init; }

        public string? Currency { get; init; }
    }
}

public class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    internal const string FeeRefusedMessage =
        "Fee cannot be set. Brokerage is added by the server from the trade fee schedule.";

    private static readonly string[] CashTypes = ["TransferIn", "TransferOut", "Interest", "CloseOut", "Opening"];
    private static readonly string[] PricedTypes = ["Buy", "Sell"];
    private static readonly string[] AllowedTypes =
    [
        "TransferIn", "TransferOut", "Interest", "CloseOut", "Opening", "OpeningHoldings",
        "Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"
    ];

    public CreateTransactionCommandValidator(ICurrentUser currentUser, TimeProvider time)
    {
        RuleFor(command => command.AccountId).NotEmpty();
        When(command => !PricedTypes.Contains(command.Type ?? ""), () =>
        {
            RuleFor(command => command.Fee)
                .Null()
                .WithMessage(FeeRefusedMessage);
            RuleFor(command => command.FxRate)
                .Null()
                .WithMessage("FxRate can only be set on a buy or sell.");
        });
        // External income R10: a hand Dividend has its own route, POST /transactions/dividend.
        RuleFor(command => command.Type)
            .Must(type => type is not null && AllowedTypes.Contains(type))
            .WithMessage("Type must be TransferIn, TransferOut, Interest, CloseOut, Opening, OpeningHoldings, Buy, Sell, StockIn, StockOut, Split, Bonus, or Scrip.");
        RuleFor(command => command.BookedAt).NotInTheFuture(time);
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= 200)
            .WithMessage("Memo must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= 100)
            .WithMessage("Reference must be at most 100 characters.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
        
        When(command => command.Type == "OpeningHoldings", () =>
        {
            RuleFor(command => command.Security)
                .NotEmpty()
                .WithMessage("Security must contain at least one leg.");
            RuleFor(command => command.Amount)
                .Null()
                .WithMessage("Amount cannot be set on OpeningHoldings.");
            RuleFor(command => command.Currency)
                .Null()
                .WithMessage("Currency cannot be set on OpeningHoldings.");
            RuleFor(command => command.Cash)
                .Null()
                .WithMessage("Cash cannot be set on OpeningHoldings.");
            RuleForEach(command => command.Security).ChildRules(leg =>
            {
                leg.RuleFor(item => item.InstrumentId).NotEmpty();
                leg.RuleFor(item => item.Quantity).GreaterThan(0);
                leg.RuleFor(item => item.Cost).NotNull().GreaterThanOrEqualTo(0);
                leg.RuleFor(item => item.Price).Null();
            });
        });

        When(command => PricedTypes.Contains(command.Type ?? ""), () =>
        {
            RuleFor(command => command.Amount).Null().WithMessage("Amount cannot be set on a buy or sell.");
            RuleFor(command => command.Currency).Null().WithMessage("Currency cannot be set on a buy or sell.");
            RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a buy or sell.");
            RuleFor(command => command.Security)
                .Must(security => security is { Count: 1 })
                .WithMessage("Security must contain exactly one leg.");
            RuleForEach(command => command.Security).ChildRules(leg =>
            {
                leg.RuleFor(item => item.InstrumentId).NotEmpty();
                leg.RuleFor(item => item.Quantity).GreaterThan(0);
                leg.RuleFor(item => item.Cost).Null().WithMessage("Cost cannot be set on a buy or sell.");
                // External caller numbers R5: the shape of the executed price; whether it is required or refused
                // depends on the custody mode, which the handler checks.
                leg.RuleFor(item => item.Price)
                    .GreaterThan(0).WithMessage("Price must be greater than 0.")
                    .Must(price => FxConversionPricing.FitsDecimalPlaces(price!.Value, 8))
                    .WithMessage("Price can have at most 8 decimal places.")
                    .When(item => item.Price is not null);
            });
            RuleFor(command => command.Fee)
                .GreaterThanOrEqualTo(0).WithMessage("Fee cannot be negative.")
                .When(command => command.Fee is not null);
            RuleFor(command => command.FxRate)
                .GreaterThan(0).WithMessage("FxRate must be greater than 0.")
                .Must(rate => FxConversionPricing.FitsDecimalPlaces(rate!.Value, 8))
                .WithMessage("FxRate can have at most 8 decimal places.")
                .When(command => command.FxRate is not null);
        });

        When(command => command.Type is "StockIn" or "StockOut", () =>
        {
            RuleFor(command => command.Amount).Null().WithMessage("Amount cannot be set on a stock movement.");
            RuleFor(command => command.Currency).Null().WithMessage("Currency cannot be set on a stock movement.");
            RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a stock movement.");
            RuleFor(command => command.Security)
                .Must(security => security is { Count: 1 })
                .WithMessage("Security must contain exactly one leg.");
            RuleForEach(command => command.Security).ChildRules(leg =>
            {
                leg.RuleFor(item => item.InstrumentId).NotEmpty();
                leg.RuleFor(item => item.Quantity).GreaterThan(0);
                leg.RuleFor(item => item.Price).Null().WithMessage("Price cannot be set on a stock movement.");
            });
        });

        When(command => command.Type == "StockIn", () =>
        {
            RuleForEach(command => command.Security).ChildRules(leg =>
                leg.RuleFor(item => item.Cost).NotNull().GreaterThanOrEqualTo(0));
        });

        When(command => command.Type == "StockOut", () =>
        {
            RuleForEach(command => command.Security).ChildRules(leg =>
                leg.RuleFor(item => item.Cost).Null().WithMessage("Cost cannot be set on a stock out."));
        });

        When(command => command.Type is "Split" or "Bonus" or "Scrip", () =>
        {
            RuleFor(command => command.Amount).Null().WithMessage("Amount cannot be set on a split, bonus, or scrip.");
            RuleFor(command => command.Currency).Null().WithMessage("Currency cannot be set on a split, bonus, or scrip.");
            RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a split, bonus, or scrip.");
            RuleFor(command => command.Ratio).Null().WithMessage("Ratio cannot be set on a split, bonus, or scrip.");
            RuleFor(command => command.Security)
                .Must(security => security is { Count: 1 })
                .WithMessage("Security must contain exactly one leg.");
            RuleForEach(command => command.Security).ChildRules(leg =>
            {
                leg.RuleFor(item => item.InstrumentId).NotEmpty();
                leg.RuleFor(item => item.Quantity).GreaterThan(0);
                leg.RuleFor(item => item.Cost).Null().WithMessage("Cost cannot be set on a split, bonus, or scrip.");
                leg.RuleFor(item => item.Price).Null().WithMessage("Price cannot be set on a split, bonus, or scrip.");
                leg.RuleFor(item => item.Ratio).Null().WithMessage("Ratio cannot be set on a split, bonus, or scrip.");
            });
        });

        When(command => CashTypes.Contains(command.Type ?? ""), () =>
        {
            RuleFor(command => command.Amount).NotNull();
            RuleFor(command => command.Security)
                .Must(security => security is null || security.Count == 0)
                .WithMessage("Security cannot be set on a cash transaction.");
        });

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

public class CreateTransactionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time)
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
                tenantId = request.TenantId,
                ratio = request.Ratio,
                fee = request.Fee,
                fxRate = request.FxRate,
                security = request.Security?.Select(leg => new
                {
                    instrumentId = leg.InstrumentId,
                    quantity = leg.Quantity,
                    cost = leg.Cost,
                    price = leg.Price,
                    ratio = leg.Ratio
                })
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

        // Account closure R12: a closing account accepts no hand write.
        await AccountClosing.EnsureNotClosingAsync(db, loaded.Account.Id, cancellationToken);

        if (loaded.Customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                loaded.Customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        var type = Enum.Parse<TransactionType>(request.Type!);

        // Custody modes R5, R6: the manual entry matrix, before every type-specific check.
        var refusal = CustodyRules.RefusalMessage(loaded.Account.CustodyMode, type);
        if (refusal is not null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTransactionCommand.Type), refusal)
            ]);
        }

        // Custody modes R10, R11: the server books a Custodial transaction; External needs the caller's instant.
        var bookedAt = BookedAtByCustody.Resolve(loaded.Account.CustodyMode, request.BookedAt, time);

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

        // CloseOut is exempt: its amount must equal the cash SUM, which a Buy or Sell can leave with
        // more decimals than the currency has (fixes/ledger-write-guards.md Decision 5).
        if (type is TransactionType.TransferIn or TransactionType.TransferOut
            or TransactionType.Interest or TransactionType.Opening)
        {
            CashAmountPlaces.EnsureFits(catalog, Money.Create(0m, currency).Currency, request.Amount!.Value);
        }

        var currentSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);

        // Withdrawals R46: on a Custodial account a CloseOut writes off only the remainder a trade leaves.
        if (type == TransactionType.CloseOut)
        {
            var closeOutCurrency = Money.Create(0m, currency).Currency;
            var closeOutRefusal = CustodyRules.CloseOutRefusal(
                loaded.Account.CustodyMode, currentSum, closeOutCurrency, catalog.TryGet(closeOutCurrency)?.DecimalPlaces ?? 2);
            if (closeOutRefusal is not null)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(CreateTransactionCommand.Type), closeOutRefusal)
                ]);
            }
        }

        var existingCount = await TransactionCash.CountAsync(db, loaded.Account.Id, cancellationToken);
        await PortfolioTradeLock.RejectIfAssignedAsync(db, loaded.Account.Id, type, cancellationToken);
        Domain.Entities.Transaction transaction;
        try
        {
            if (type == TransactionType.OpeningHoldings)
            {
                var legs = new List<OpeningSecurityLeg>();
                foreach (var item in request.Security!)
                {
                    var instrument = await db.Instruments.SingleOrDefaultAsync(
                        row => row.PublicId == item.InstrumentId && row.TenantId == tenant.Id,
                        cancellationToken) ?? throw new NotFoundException("Instrument", item.InstrumentId);

                    if (!instrument.IsActive)
                    {
                        throw new ValidationException([
                            new ValidationFailure(nameof(CreateTransactionCommand.Security), "Instrument is disabled.")
                        ]);
                    }

                    legs.Add(OpeningSecurityLeg.Create(
                        instrument.Id, item.Quantity, item.Cost!.Value, instrument.QuoteCurrency.Trim()));
                }

                var instrumentIds = legs.Select(leg => leg.InstrumentId).ToList();
                var alreadyOpened = await (
                    from securityLeg in db.TransactionSecurityLegs
                    where securityLeg.AccountId == loaded.Account.Id
                          && instrumentIds.Contains(securityLeg.InstrumentId)
                    select securityLeg.Id).AnyAsync(cancellationToken);
                if (alreadyOpened)
                {
                    throw new ValidationException([
                        new ValidationFailure(
                            nameof(CreateTransactionCommand.Security),
                            "OpeningHoldings must be the first security activity for that instrument.")
                    ]);
                }

                transaction = Domain.Entities.Transaction.PostOpeningHoldings(
                    tenant.Id,
                    loaded.Account.Id,
                    loaded.Account.Type,
                    bookedAt,
                    request.Memo,
                    request.Reference,
                    TransactionSource.Manual,
                    legs);
            }
            else if (type is TransactionType.Buy or TransactionType.Sell)
            {
                transaction = await PostPricedAsync(
                    request, bookedAt, tenant, loaded, type, currency, currentSum, cancellationToken);
            }
            else if (type is TransactionType.StockIn or TransactionType.StockOut)
            {
                transaction = await PostStockAsync(request, bookedAt, tenant, loaded, type, cancellationToken);
            }
            else if (type is TransactionType.Split or TransactionType.Bonus or TransactionType.Scrip)
            {
                transaction = await PostSplitAsync(request, bookedAt, tenant, loaded, type, cancellationToken);
            }
            else
            {
                transaction = Domain.Entities.Transaction.Post(
                    tenant.Id,
                    loaded.Account.Id,
                    type,
                    request.Amount!.Value,
                    currency,
                    bookedAt,
                    request.Memo,
                    request.Reference,
                    TransactionSource.Manual,
                    currentSum,
                    existingCount);
            }
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

        if (type is TransactionType.OpeningHoldings or TransactionType.Buy or TransactionType.Sell
            or TransactionType.StockIn or TransactionType.StockOut
            or TransactionType.Split or TransactionType.Bonus or TransactionType.Scrip)
        {
            try
            {
                await HoldingProjection.SyncAsync(
                    db,
                    tenant.Id,
                    loaded.Account.Id,
                    transaction.SecurityLegs.Select(leg => leg.InstrumentId),
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DomainException exception)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, exception.Message)
                ]);
            }
        }

        var postedSum = await TransactionCash.SumAsync(db, loaded.Account.Id, cancellationToken);
        if (postedSum < 0 || (type == TransactionType.CloseOut && postedSum != 0))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        await AvailableCashGuard.EnsureAsync(db, catalog, loaded.Account, postedSum, cancellationToken);

        return new IdempotentResult(201, body);
    }

    private async Task<Domain.Entities.Transaction> PostPricedAsync(
        CreateTransactionCommand request,
        DateTimeOffset bookedAt,
        Tenant tenant,
        (Account Account, User Customer) loaded,
        TransactionType type,
        string accountCurrency,
        decimal currentCashSum,
        CancellationToken cancellationToken)
    {
        var item = request.Security!.Single();
        var instrument = await db.Instruments.SingleOrDefaultAsync(
            row => row.PublicId == item.InstrumentId && row.TenantId == tenant.Id,
            cancellationToken) ?? throw new NotFoundException("Instrument", item.InstrumentId);
        if (!instrument.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTransactionCommand.Security), "Instrument is disabled.")
            ]);
        }

        var quote = instrument.QuoteCurrency.Trim().ToUpperInvariant();
        var quoteDecimalPlaces = catalog.TryGet(quote)?.DecimalPlaces ?? 2;
        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == loaded.Account.Id && row.InstrumentId == instrument.Id,
            cancellationToken);
        // Orders R48: the matrix refuses a hand Buy or Sell on a Custodial account, so this trade is on an External one.
        return PostExternalTrade(
            request, item, instrument.Id, bookedAt, tenant, loaded, type, accountCurrency, quote,
            quoteDecimalPlaces, currentCashSum, holding);
    }

    private const string PriceKey = "Security[0].Price";

    /// <summary>
    /// External caller numbers R5–R10: an External <c>Buy</c> / <c>Sell</c> uses the caller's executed price, fee and
    /// FX rate. No stored price, rate or trade fee schedule is read.
    /// </summary>
    private static Domain.Entities.Transaction PostExternalTrade(
        CreateTransactionCommand request,
        CreateTransactionCommand.OpeningHoldingSecurity item,
        int instrumentId,
        DateTimeOffset bookedAt,
        Tenant tenant,
        (Account Account, User Customer) loaded,
        TransactionType type,
        string accountCurrency,
        string quote,
        int quoteDecimalPlaces,
        decimal currentCashSum,
        Holding? holding)
    {
        var bookingCurrency = Money.Create(0m, accountCurrency).Currency;
        var crossCurrency = !string.Equals(quote, bookingCurrency, StringComparison.Ordinal);
        var failures = new List<ValidationFailure>();
        if (item.Price is null)
        {
            failures.Add(new ValidationFailure(
                PriceKey, "Price is required on an External account. Enter the executed price."));
        }

        if (request.Fee is { } fee && !FxConversionPricing.FitsDecimalPlaces(fee, quoteDecimalPlaces))
        {
            failures.Add(new ValidationFailure(
                nameof(CreateTransactionCommand.Fee), $"Fee can have at most {quoteDecimalPlaces} decimal places."));
        }

        if (crossCurrency && request.FxRate is null)
        {
            failures.Add(new ValidationFailure(
                nameof(CreateTransactionCommand.FxRate),
                "FxRate is required when the instrument currency differs from the account currency."));
        }
        else if (!crossCurrency && request.FxRate is not null)
        {
            failures.Add(new ValidationFailure(
                nameof(CreateTransactionCommand.FxRate),
                "FxRate cannot be set when the instrument currency is the account currency."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var price = Money.Create(item.Price!.Value, quote);
        var fxRate = request.FxRate ?? 1m;
        var appliedRate = crossCurrency
            ? AppliedFxRate.Create(
                quote, bookingCurrency, fxRate, FxConversionPricing.DateOf(bookedAt), AppliedFxRate.CallerSource)
            : null;
        return type == TransactionType.Buy
            ? Domain.Entities.Transaction.PostBuy(
                tenant.Id,
                loaded.Account.Id,
                loaded.Account.Type,
                bookingCurrency,
                instrumentId,
                item.Quantity,
                price,
                fxRate,
                bookedAt,
                request.Memo,
                request.Reference,
                TransactionSource.Manual,
                currentCashSum,
                feeSchedule: null,
                quoteDecimalPlaces,
                request.Fee,
                appliedRate)
            : Domain.Entities.Transaction.PostSell(
                tenant.Id,
                loaded.Account.Id,
                loaded.Account.Type,
                bookingCurrency,
                instrumentId,
                item.Quantity,
                price,
                fxRate,
                bookedAt,
                request.Memo,
                request.Reference,
                TransactionSource.Manual,
                currentCashSum,
                holding?.Quantity ?? 0m,
                holding?.CostAmount ?? 0m,
                feeSchedule: null,
                quoteDecimalPlaces,
                request.Fee,
                appliedRate);
    }

    /// <summary>External caller numbers R13: a Custodial account refuses the caller's price, fee and FX rate.</summary>
    private async Task<Domain.Entities.Transaction> PostStockAsync(
        CreateTransactionCommand request,
        DateTimeOffset bookedAt,
        Tenant tenant,
        (Account Account, User Customer) loaded,
        TransactionType type,
        CancellationToken cancellationToken)
    {
        var item = request.Security!.Single();
        var instrument = await db.Instruments.SingleOrDefaultAsync(
            row => row.PublicId == item.InstrumentId && row.TenantId == tenant.Id,
            cancellationToken) ?? throw new NotFoundException("Instrument", item.InstrumentId);
        if (!instrument.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTransactionCommand.Security), "Instrument is disabled.")
            ]);
        }

        var quote = instrument.QuoteCurrency.Trim();
        if (type == TransactionType.StockIn)
        {
            return Domain.Entities.Transaction.PostStockIn(
                tenant.Id,
                loaded.Account.Id,
                loaded.Account.Type,
                instrument.Id,
                item.Quantity,
                item.Cost!.Value,
                quote,
                bookedAt,
                request.Memo,
                request.Reference,
                TransactionSource.Manual);
        }

        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == loaded.Account.Id && row.InstrumentId == instrument.Id,
            cancellationToken);
        return Domain.Entities.Transaction.PostStockOut(
            tenant.Id,
            loaded.Account.Id,
            loaded.Account.Type,
            instrument.Id,
            item.Quantity,
            quote,
            bookedAt,
            request.Memo,
            request.Reference,
            TransactionSource.Manual,
            holding?.Quantity ?? 0m,
            holding?.CostAmount ?? 0m);
    }

    private async Task<Domain.Entities.Transaction> PostSplitAsync(
        CreateTransactionCommand request,
        DateTimeOffset bookedAt,
        Tenant tenant,
        (Account Account, User Customer) loaded,
        TransactionType type,
        CancellationToken cancellationToken)
    {
        var item = request.Security!.Single();
        var instrument = await db.Instruments.SingleOrDefaultAsync(
            row => row.PublicId == item.InstrumentId && row.TenantId == tenant.Id,
            cancellationToken) ?? throw new NotFoundException("Instrument", item.InstrumentId);
        if (!instrument.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTransactionCommand.Security), "Instrument is disabled.")
            ]);
        }

        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == loaded.Account.Id && row.InstrumentId == instrument.Id,
            cancellationToken);
        return Domain.Entities.Transaction.PostSplit(
            tenant.Id,
            loaded.Account.Id,
            loaded.Account.Type,
            type,
            instrument.Id,
            item.Quantity,
            instrument.QuoteCurrency,
            bookedAt,
            request.Memo,
            request.Reference,
            TransactionSource.Manual,
            holding?.Quantity ?? 0m,
            holding?.CostAmount ?? 0m);
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

using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Corporate-actions R15–R17: a TenantAdmin enters a corporate action of an instrument no connection serves.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateCorporateActionCommand : IRequest<Guid>, ICorporateActionTermsBody
{
    public Guid? InstrumentId { get; init; }

    public string? Kind { get; init; }

    public int? RatioNew { get; init; }

    public int? RatioHeld { get; init; }

    /// <summary>Dividends R3, R11: the terms of a dividend, in place of the ratio.</summary>
    public decimal? AmountPerShare { get; init; }

    public string? TaxSource { get; init; }

    public decimal? ImputationCreditPerShare { get; init; }

    public decimal? ForeignTaxRatePercent { get; init; }

    /// <summary>Dividends R7: refused by hand; a plan comes only from the feed.</summary>
    public decimal? ReinvestmentPrice { get; init; }

    public DateOnly? ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public string? Memo { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Corporate-actions R18: a TenantAdmin changes a <c>Manual</c> action that is still <c>Announced</c>.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateCorporateActionCommand : IRequest, ICorporateActionTermsBody
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Kind { get; init; }

    public int? RatioNew { get; init; }

    public int? RatioHeld { get; init; }

    /// <summary>Dividends R3, R11: the terms of a dividend, in place of the ratio.</summary>
    public decimal? AmountPerShare { get; init; }

    public string? TaxSource { get; init; }

    public decimal? ImputationCreditPerShare { get; init; }

    public decimal? ForeignTaxRatePercent { get; init; }

    /// <summary>Dividends R7: refused by hand; a plan comes only from the feed.</summary>
    public decimal? ReinvestmentPrice { get; init; }

    public DateOnly? ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public string? Memo { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Corporate-actions R19: a TenantAdmin cancels a <c>Manual</c> action that is still <c>Announced</c>.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CancelCorporateActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Corporate-actions R20: a TenantAdmin makes a <c>Manual</c> action effective on or after its ex-date,
/// for an instrument whose price does not arrive. The apply job posts the entitlements (R24).</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ApplyCorporateActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class ApplyCorporateActionCommandValidator : AbstractValidator<ApplyCorporateActionCommand>
{
    public ApplyCorporateActionCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CreateCorporateActionCommandValidator : AbstractValidator<CreateCorporateActionCommand>
{
    public CreateCorporateActionCommandValidator()
    {
        RuleFor(command => command.InstrumentId).NotNull();
        RuleFor(command => command.Kind).Must(CorporateActionTerms.IsKind).WithMessage(CorporateActionTerms.KindMessage);
        Include(new CorporateActionTermsValidator());
        RuleFor(command => command.ExDate).NotNull();
        RuleFor(command => command.Memo).Must(CorporateActionTerms.IsMemo).WithMessage(CorporateActionTerms.MemoMessage);
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class UpdateCorporateActionCommandValidator : AbstractValidator<UpdateCorporateActionCommand>
{
    public UpdateCorporateActionCommandValidator()
    {
        // Dividends R11: the update of a dividend may leave the kind out; the kind never changes.
        RuleFor(command => command.Kind)
            .Must(kind => kind is null || CorporateActionTerms.IsKind(kind))
            .WithMessage(CorporateActionTerms.KindMessage);
        Include(new CorporateActionTermsValidator());
        RuleFor(command => command.ExDate).NotNull();
        RuleFor(command => command.Memo).Must(CorporateActionTerms.IsMemo).WithMessage(CorporateActionTerms.MemoMessage);
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CancelCorporateActionCommandValidator : AbstractValidator<CancelCorporateActionCommand>
{
    public CancelCorporateActionCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note?.Trim().Length is >= 1 and <= CorporateAction.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CreateCorporateActionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    MarketDataConnections connections,
    TimeProvider time)
    : IRequestHandler<CreateCorporateActionCommand, Guid>
{
    public async Task<Guid> Handle(CreateCorporateActionCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        var instrumentId = request.InstrumentId!.Value;
        var instrument = await db.Instruments.AsNoTracking()
                             .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.PublicId == instrumentId, cancellationToken)
                         ?? throw new NotFoundException("Instrument", instrumentId);

        // R16: a served instrument's corporate actions come from its connection's feed only.
        if (connections.For(instrument.Symbol) is not null)
        {
            throw CorporateActionReader.Error(
                nameof(CreateCorporateActionCommand.InstrumentId),
                $"Corporate actions of {instrument.Symbol} come from its market data connection.");
        }

        var kind = CorporateActionTerms.ParseKind(request.Kind!);
        var exDate = request.ExDate!.Value;
        CorporateAction action;
        if (kind == CorporateActionKind.Dividend)
        {
            // Dividends R11: the terms in place of the ratio; a plan comes only from the feed (R7).
            var terms = CorporateActionTerms.Dividend(request);
            CorporateActionTerms.EnsureExDate(exDate, time);
            await CorporateActionTerms.EnsureNoDuplicateAsync(db, instrument, kind, exDate, null, cancellationToken);
            action = CorporateAction.EnterDividend(
                tenantId, instrument.Id, terms, exDate, request.RecordDate, request.PaymentDate, request.Memo);
        }
        else
        {
            var ratio = CorporateActionTerms.Ratio(kind, request.RatioNew!.Value, request.RatioHeld!.Value);
            CorporateActionTerms.EnsureExDate(exDate, time);
            await CorporateActionTerms.EnsureNoDuplicateAsync(db, instrument, kind, exDate, null, cancellationToken);
            action = CorporateAction.Enter(tenantId, instrument.Id, kind, ratio, exDate, request.RecordDate, request.PaymentDate, request.Memo);
        }

        db.CorporateActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        return action.PublicId;
    }
}

public class UpdateCorporateActionCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<UpdateCorporateActionCommand>
{
    public async Task Handle(UpdateCorporateActionCommand request, CancellationToken cancellationToken)
    {
        var action = await CorporateActionReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        CorporateActionReader.EnsureChangeable(action, request.RowVersion, "changed");
        var instrument = await db.Instruments.AsNoTracking().SingleAsync(row => row.Id == action.InstrumentId, cancellationToken);

        var exDate = request.ExDate!.Value;
        var isDividend = action.Kind == CorporateActionKind.Dividend;
        if (CorporateActionTerms.IsDividendBody(request) != isDividend)
        {
            // Dividends R11: the kind of a corporate action never changes to or from a dividend.
            throw CorporateActionReader.Error(
                nameof(UpdateCorporateActionCommand.Kind),
                isDividend ? "The kind of a dividend never changes." : "A corporate action does not become a dividend.");
        }

        if (isDividend)
        {
            var terms = CorporateActionTerms.Dividend(request);
            CorporateActionTerms.EnsureExDate(exDate, time);
            await CorporateActionTerms.EnsureNoDuplicateAsync(db, instrument, action.Kind, exDate, action.Id, cancellationToken);
            action.ChangeDividend(terms, exDate, request.RecordDate, request.PaymentDate, request.Memo);
            await CorporateActionReader.SaveAsync(db, cancellationToken);
            return;
        }

        var kind = request.Kind is null ? action.Kind : CorporateActionTerms.ParseKind(request.Kind);
        var ratio = CorporateActionTerms.Ratio(kind, request.RatioNew!.Value, request.RatioHeld!.Value);
        CorporateActionTerms.EnsureExDate(exDate, time);
        await CorporateActionTerms.EnsureNoDuplicateAsync(db, instrument, kind, exDate, action.Id, cancellationToken);

        action.Change(kind, ratio, exDate, request.RecordDate, request.PaymentDate, request.Memo);
        await CorporateActionReader.SaveAsync(db, cancellationToken);
    }
}

public class CancelCorporateActionCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<CancelCorporateActionCommand>
{
    public async Task Handle(CancelCorporateActionCommand request, CancellationToken cancellationToken)
    {
        var action = await CorporateActionReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        CorporateActionReader.EnsureChangeable(action, request.RowVersion, "cancelled");
        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();

        action.Cancel(actor, request.Note!, time.GetUtcNow());
        await CorporateActionReader.SaveAsync(db, cancellationToken);
    }
}

public class ApplyCorporateActionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    TimeProvider time)
    : IRequestHandler<ApplyCorporateActionCommand>
{
    public async Task Handle(ApplyCorporateActionCommand request, CancellationToken cancellationToken)
    {
        var action = await CorporateActionReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        CorporateActionReader.EnsureChangeable(action, request.RowVersion, "applied");
        var now = time.GetUtcNow();
        if (DateOnly.FromDateTime(now.UtcDateTime) < action.ExDate)
        {
            throw CorporateActionReader.Error(
                nameof(CorporateAction.ExDate), "A corporate action cannot be applied before its ex-date.");
        }

        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        await db.ExecuteInTransactionAsync(async token =>
        {
            await CorporateActionEffect.MakeEffectiveAsync(db, currencies, action, now, actor, token);
            await CorporateActionReader.SaveAsync(db, token);
        }, cancellationToken);
    }
}

/// <summary>Dividends R31: a TenantAdmin pays in cash a dividend whose accepted election delivered no shares.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PayDividendInCashCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }
}

public class PayDividendInCashCommandHandler : IRequestHandler<PayDividendInCashCommand>
{
    public Task Handle(PayDividendInCashCommand request, CancellationToken cancellationToken) => throw new NotImplementedException();
}

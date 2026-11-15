using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Fees.TradeSchedules.Commands;

/// <summary>Replace a pending trade fee schedule row (trade-fees R11). <c>quoteCurrency</c> is immutable.</summary>
[Authorize(Policy = Policies.FeesManage)]
public class UpdateTradeFeeScheduleCommand : IRequest
{
    public Guid Id { get; set; }

    public DateOnly? EffectiveFrom { get; init; }

    public decimal? Minimum { get; init; }

    public decimal? Cap { get; init; }

    public List<FeeBandInput>? Bands { get; init; }

    public string? RowVersion { get; init; }
}

public class UpdateTradeFeeScheduleCommandValidator : AbstractValidator<UpdateTradeFeeScheduleCommand>
{
    public UpdateTradeFeeScheduleCommandValidator()
    {
        RuleFor(command => command.EffectiveFrom).NotNull();
        RuleFor(command => command.Minimum).NotNull();
        RuleFor(command => command.Bands).NotNull();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class UpdateTradeFeeScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<UpdateTradeFeeScheduleCommand>
{
    public async Task Handle(UpdateTradeFeeScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await TradeFeeScheduleLoader.LoadAsync(db, currentUser, request.Id, tracking: true, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(schedule.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var cutOff = await TradeFeeCutOff.ForAsync(
            db, schedule.TenantId, schedule.QuoteCurrency, TradeFeeCutOff.Today(time), cancellationToken);
        if (schedule.IsInEffect(cutOff))
        {
            throw new FeeScheduleInEffectException();
        }

        var decimalPlaces = catalog.TryGet(schedule.QuoteCurrency)?.DecimalPlaces ?? 4;
        var failures = TradeFeeScheduleRules.Check(request.Minimum!.Value, request.Cap, request.Bands, decimalPlaces);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var effectiveFrom = request.EffectiveFrom!.Value;
        EffectiveFromRules.EnsureAfterCutOff(effectiveFrom, cutOff);
        if (await TradeFeeScheduleLoader.DateTakenAsync(
                db, schedule.TenantId, schedule.QuoteCurrency, effectiveFrom, schedule.Id, cancellationToken))
        {
            throw EffectiveFromRules.Duplicate();
        }

        var previous = schedule.BandRows.ToList();
        try
        {
            schedule.Update(
                effectiveFrom, request.Minimum.Value, request.Cap,
                TradeFeeScheduleRules.ToBands(request.Bands!), decimalPlaces, cutOff);
        }
        catch (TradeFeeScheduleInEffectException)
        {
            throw new FeeScheduleInEffectException();
        }
        catch (DomainException exception)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateTradeFeeScheduleCommand.Bands), exception.Message)]);
        }

        db.TradeFeeBands.RemoveRange(previous);
        await db.SaveChangesAsync(cancellationToken);
    }
}

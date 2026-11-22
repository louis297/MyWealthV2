using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R13: replace the date and the whole band set of a pending row. <c>currency</c> is immutable.</summary>
[Authorize(Policy = Policies.InterestRatesManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UpdateInterestRateScheduleCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public DateOnly? EffectiveFrom { get; init; }

    public List<FeeBandInput>? Bands { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? Currency { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class UpdateInterestRateScheduleCommandValidator : AbstractValidator<UpdateInterestRateScheduleCommand>
{
    public UpdateInterestRateScheduleCommandValidator()
    {
        RuleFor(command => command.EffectiveFrom).NotNull();
        RuleFor(command => command.Bands).NotNull();
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Currency)
            .Must(currency => currency is null)
            .WithMessage("Currency cannot be changed.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class UpdateInterestRateScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<UpdateInterestRateScheduleCommand>
{
    public async Task Handle(UpdateInterestRateScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await InterestRateScheduleRules.LoadAsync(db, currentUser, request.Id, tracking: true, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(schedule.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var cutOff = await InterestCutOff.ForAsync(db, schedule.TenantId, schedule.Currency, InterestCutOff.Today(time), cancellationToken);
        if (schedule.IsInEffect(cutOff))
        {
            throw new InterestScheduleInEffectProblem();
        }

        var decimalPlaces = catalog.TryGet(schedule.Currency)?.DecimalPlaces ?? 4;
        InterestRateScheduleRules.EnsureBands(request.Bands, decimalPlaces);
        var effectiveFrom = request.EffectiveFrom!.Value;
        await InterestRateScheduleRules.EnsureDateAsync(
            db, schedule.TenantId, schedule.Currency, effectiveFrom, cutOff, schedule.Id, cancellationToken);

        var previous = schedule.BandRows.ToList();
        try
        {
            schedule.Update(effectiveFrom, TradeFeeScheduleRules.ToBands(request.Bands!), decimalPlaces, cutOff);
        }
        catch (InterestScheduleInEffectException)
        {
            throw new InterestScheduleInEffectProblem();
        }
        catch (DomainException exception)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateInterestRateScheduleCommand.Bands), exception.Message)]);
        }

        db.InterestRateBands.RemoveRange(previous);
        await db.SaveChangesAsync(cancellationToken);
    }
}

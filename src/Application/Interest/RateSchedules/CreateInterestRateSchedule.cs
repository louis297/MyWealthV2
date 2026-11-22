using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R9, R10, R12, R13: create an interest rate schedule row.</summary>
[Authorize(Policy = Policies.InterestRatesManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateInterestRateScheduleCommand : IRequest<Guid>
{
    public string? Currency { get; init; }

    public DateOnly? EffectiveFrom { get; init; }

    public List<FeeBandInput>? Bands { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class CreateInterestRateScheduleCommandValidator : AbstractValidator<CreateInterestRateScheduleCommand>
{
    public CreateInterestRateScheduleCommandValidator()
    {
        RuleFor(command => command.Currency).NotEmpty();
        RuleFor(command => command.EffectiveFrom).NotNull();
        RuleFor(command => command.Bands).NotNull();
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CreateInterestRateScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<CreateInterestRateScheduleCommand, Guid>
{
    public async Task<Guid> Handle(CreateInterestRateScheduleCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        var currency = catalog.TryGet(request.Currency!.Trim().ToUpperInvariant());
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateInterestRateScheduleCommand.Currency), "Currency must be an enabled catalog code.")
            ]);
        }

        InterestRateScheduleRules.EnsureBands(request.Bands, currency.DecimalPlaces);
        var effectiveFrom = request.EffectiveFrom!.Value;
        var cutOff = await InterestCutOff.ForAsync(db, tenantId, currency.Code, InterestCutOff.Today(time), cancellationToken);
        await InterestRateScheduleRules.EnsureDateAsync(db, tenantId, currency.Code, effectiveFrom, cutOff, null, cancellationToken);

        InterestRateSchedule schedule;
        try
        {
            schedule = InterestRateSchedule.Create(tenantId, currency, effectiveFrom, TradeFeeScheduleRules.ToBands(request.Bands!), cutOff);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateInterestRateScheduleCommand.Bands), exception.Message)]);
        }

        db.InterestRateSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return schedule.PublicId;
    }
}

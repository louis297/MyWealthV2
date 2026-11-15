using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Fees.TradeSchedules.Commands;

/// <summary>Create a trade fee schedule row (trade-fees R3–R6, R10).</summary>
[Authorize(Policy = Policies.FeesManage)]
public class CreateTradeFeeScheduleCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public string? QuoteCurrency { get; init; }

    public DateOnly? EffectiveFrom { get; init; }

    public decimal? Minimum { get; init; }

    public decimal? Cap { get; init; }

    public List<FeeBandInput>? Bands { get; init; }
}

public class CreateTradeFeeScheduleCommandValidator : AbstractValidator<CreateTradeFeeScheduleCommand>
{
    public CreateTradeFeeScheduleCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.QuoteCurrency).NotEmpty();
        RuleFor(command => command.EffectiveFrom).NotNull();
        RuleFor(command => command.Minimum).NotNull();
        RuleFor(command => command.Bands).NotNull();
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class CreateTradeFeeScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<CreateTradeFeeScheduleCommand, Guid>
{
    public async Task<Guid> Handle(CreateTradeFeeScheduleCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PortfolioTenant.ResolveAsync(db, currentUser, request.TenantId, cancellationToken);
        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a trade fee schedule while the tenant is disabled. Enable the tenant first.");
        }

        var currency = catalog.TryGet(request.QuoteCurrency!.Trim());
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTradeFeeScheduleCommand.QuoteCurrency),
                    "Quote currency must be an enabled catalog code.")
            ]);
        }

        var failures = TradeFeeScheduleRules.Check(request.Minimum!.Value, request.Cap, request.Bands, currency.DecimalPlaces);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var effectiveFrom = request.EffectiveFrom!.Value;
        var cutOff = await TradeFeeCutOff.ForAsync(
            db, tenant.Id, currency.Code, TradeFeeCutOff.Today(time), cancellationToken);
        EffectiveFromRules.EnsureAfterCutOff(effectiveFrom, cutOff);
        if (await TradeFeeScheduleLoader.DateTakenAsync(db, tenant.Id, currency.Code, effectiveFrom, null, cancellationToken))
        {
            throw EffectiveFromRules.Duplicate();
        }

        TradeFeeSchedule schedule;
        try
        {
            schedule = TradeFeeSchedule.Create(
                tenant.Id, currency, effectiveFrom, request.Minimum.Value, request.Cap,
                TradeFeeScheduleRules.ToBands(request.Bands!), cutOff);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateTradeFeeScheduleCommand.Bands), exception.Message)]);
        }

        db.TradeFeeSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return schedule.PublicId;
    }
}

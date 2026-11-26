using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R15, R24: a SystemAdmin runs the valuations of a Tenant through a date; it runs at once.</summary>
[Authorize(Policy = Policies.ValuationRunsStart)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class StartValuationRunCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public string? ThroughDate { get; init; }
}

/// <summary>R15: <c>tenantId</c> is required; <c>throughDate</c> is a <c>yyyy-MM-dd</c> date not after the last final
/// day. There is no option that allows a later date.</summary>
public class StartValuationRunCommandValidator : AbstractValidator<StartValuationRunCommand>
{
    public StartValuationRunCommandValidator(TimeProvider time)
    {
        RuleFor(command => command.TenantId).NotEmpty().WithMessage("TenantId is required.");
        RuleFor(command => command.ThroughDate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("ThroughDate is required.")
            .Must(MarketDataDates.IsEmptyOrDate).WithMessage($"ThroughDate {MarketDataDates.FormatMessage}")
            .Must(date => MarketDataDates.Parse(date) <= ValuationRules.LastFinalDay(LedgerDay.Of(time.GetUtcNow())))
            .WithMessage(StartValuationRunCommandHandler.NotFinalMessage);
    }
}

public class StartValuationRunCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IRunValuations runValuations)
    : IRequestHandler<StartValuationRunCommand, Guid>
{
    public const string NotFinalMessage = "A valuation can be stored only for a day whose prices and rates are final.";

    public async Task<Guid> Handle(StartValuationRunCommand request, CancellationToken cancellationToken)
    {
        // R15: an unknown tenant is 404; a disabled one is run.
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId!);
        var throughDate = MarketDataDates.Parse(request.ThroughDate)!.Value;
        var run = await runValuations.RunAsync(
            tenant.Id, throughDate, ValuationRunTrigger.Manual, currentUser.Person?.Id, cancellationToken);
        return run.PublicId;
    }
}

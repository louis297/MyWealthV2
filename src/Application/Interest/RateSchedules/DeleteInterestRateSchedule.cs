using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R13: delete a pending interest rate schedule row.</summary>
[Authorize(Policy = Policies.InterestRatesManage)]
public record DeleteInterestRateScheduleCommand(Guid Id) : IRequest;

public class DeleteInterestRateScheduleCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<DeleteInterestRateScheduleCommand>
{
    public async Task Handle(DeleteInterestRateScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await InterestRateScheduleRules.LoadAsync(db, currentUser, request.Id, tracking: true, cancellationToken);
        var cutOff = await InterestCutOff.ForAsync(db, schedule.TenantId, schedule.Currency, InterestCutOff.Today(time), cancellationToken);
        try
        {
            schedule.EnsureDeletable(cutOff);
        }
        catch (InterestScheduleInEffectException)
        {
            throw new InterestScheduleInEffectProblem();
        }

        db.InterestRateBands.RemoveRange(schedule.BandRows);
        db.InterestRateSchedules.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }
}

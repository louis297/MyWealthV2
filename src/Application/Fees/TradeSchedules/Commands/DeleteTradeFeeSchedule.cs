using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Application.Fees.TradeSchedules.Commands;

/// <summary>Delete a pending trade fee schedule row (trade-fees R11).</summary>
[Authorize(Policy = Policies.FeesManage)]
public record DeleteTradeFeeScheduleCommand(Guid Id) : IRequest;

public class DeleteTradeFeeScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<DeleteTradeFeeScheduleCommand>
{
    public async Task Handle(DeleteTradeFeeScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await TradeFeeScheduleLoader.LoadAsync(db, currentUser, request.Id, tracking: true, cancellationToken);
        var cutOff = await TradeFeeCutOff.ForAsync(
            db, schedule.TenantId, schedule.QuoteCurrency, TradeFeeCutOff.Today(time), cancellationToken);
        try
        {
            schedule.EnsureDeletable(cutOff);
        }
        catch (TradeFeeScheduleInEffectException)
        {
            throw new FeeScheduleInEffectException();
        }

        db.TradeFeeBands.RemoveRange(schedule.BandRows);
        db.TradeFeeSchedules.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }
}

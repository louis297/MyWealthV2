using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Fees.TradeSchedules.Queries;

/// <summary>Get one trade fee schedule row by PublicId (trade-fees §8).</summary>
[Authorize(Policy = Policies.FeesRead)]
public record GetTradeFeeScheduleByIdQuery(Guid Id) : IRequest<TradeFeeScheduleDto>;

public class GetTradeFeeScheduleByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetTradeFeeScheduleByIdQuery, TradeFeeScheduleDto>
{
    public async Task<TradeFeeScheduleDto> Handle(GetTradeFeeScheduleByIdQuery request, CancellationToken cancellationToken)
    {
        var schedule = await TradeFeeScheduleLoader.LoadAsync(db, currentUser, request.Id, tracking: false, cancellationToken);
        var cutOff = await TradeFeeCutOff.ForAsync(
            db, schedule.TenantId, schedule.QuoteCurrency, TradeFeeCutOff.Today(time), cancellationToken);
        return TradeFeeScheduleDto.From(schedule, cutOff);
    }
}

using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.MarketData;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Instruments.Queries.GetInstrumentById;

[Authorize(Policy = Policies.InstrumentsRead)]
public record GetInstrumentByIdQuery(Guid Id) : IRequest<InstrumentDto>;

public class GetInstrumentByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, MarketDataConnections connections)
    : IRequestHandler<GetInstrumentByIdQuery, InstrumentDto>
{
    public async Task<InstrumentDto> Handle(GetInstrumentByIdQuery request, CancellationToken cancellationToken)
    {
        var query =
            from instrument in InstrumentVisibility.Readable(db, currentUser).Include(row => row.FifTreatments)
            join tenant in db.Tenants.AsNoTracking() on instrument.TenantId equals tenant.Id
            where instrument.PublicId == request.Id
            select new { instrument, tenant };

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Instrument", request.Id);

        return InstrumentDto.From(row.instrument, row.tenant, connections.For(row.instrument.Symbol)?.Name);
    }
}

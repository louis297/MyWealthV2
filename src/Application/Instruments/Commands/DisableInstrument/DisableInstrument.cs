using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Instruments.Commands.DisableInstrument;

[Authorize(Policy = Policies.InstrumentsManage)]
public class DisableInstrumentCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableInstrumentCommandValidator : AbstractValidator<DisableInstrumentCommand>
{
    public DisableInstrumentCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableInstrumentCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DisableInstrumentCommand>
{
    public async Task Handle(DisableInstrumentCommand request, CancellationToken cancellationToken)
    {
        var query = db.Instruments.Where(row => row.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.TenantId == currentUser.TenantId);
        }

        var instrument = await query.SingleOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("Instrument", request.Id);

        var expected = Convert.ToBase64String(instrument.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        instrument.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}

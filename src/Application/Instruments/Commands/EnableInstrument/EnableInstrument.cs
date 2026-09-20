using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Instruments.Commands.EnableInstrument;

[Authorize(Policy = Policies.InstrumentsManage)]
public class EnableInstrumentCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableInstrumentCommandValidator : AbstractValidator<EnableInstrumentCommand>
{
    public EnableInstrumentCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class EnableInstrumentCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<EnableInstrumentCommand>
{
    public async Task Handle(EnableInstrumentCommand request, CancellationToken cancellationToken)
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

        instrument.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}

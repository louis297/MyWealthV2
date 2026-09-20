using System.Text.RegularExpressions;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Instruments.Commands.UpdateInstrument;

[Authorize(Policy = Policies.InstrumentsManage)]
public class UpdateInstrumentCommand : IRequest
{
    public Guid Id { get; set; }

    public string? Name { get; init; }

    public string? Symbol { get; init; }

    public string? RowVersion { get; init; }

    public string? QuoteCurrency { get; init; }

    public Guid? TenantId { get; init; }

    public bool? IsEnabled { get; init; }
}

public class UpdateInstrumentCommandValidator : AbstractValidator<UpdateInstrumentCommand>
{
    private static readonly Regex SymbolPattern = new("^[A-Z0-9.-]{1,32}$", RegexOptions.CultureInvariant);

    public UpdateInstrumentCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.QuoteCurrency)
            .Must(quoteCurrency => quoteCurrency is null)
            .WithMessage("Quote currency cannot be changed.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be changed.");
        RuleFor(command => command.IsEnabled)
            .Must(isEnabled => isEnabled is null)
            .WithMessage("IsEnabled cannot be changed.");
        RuleFor(command => command.Name)
            .Must(name => name is null || (!string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200))
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Symbol)
            .Must(symbol =>
            {
                if (symbol is null)
                {
                    return true;
                }

                var normalised = symbol.Trim().ToUpperInvariant();
                return SymbolPattern.IsMatch(normalised);
            })
            .WithMessage("Symbol must be 1 to 32 characters in [A-Z0-9.-].");
        RuleFor(command => command)
            .Must(command => command.Name is not null || command.Symbol is not null)
            .WithMessage("Name or symbol is required.");
    }
}

public class UpdateInstrumentCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateInstrumentCommand>
{
    public async Task Handle(UpdateInstrumentCommand request, CancellationToken cancellationToken)
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

        if (request.Symbol is not null)
        {
            var symbol = request.Symbol.Trim().ToUpperInvariant();
            if (await db.Instruments.AnyAsync(
                    row => row.TenantId == instrument.TenantId
                           && row.Id != instrument.Id
                           && row.Symbol == symbol,
                    cancellationToken))
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(UpdateInstrumentCommand.Symbol),
                        "Symbol must be unique inside the tenant.")
                ]);
            }
        }

        instrument.Rename(request.Name, request.Symbol);
        await db.SaveChangesAsync(cancellationToken);
    }
}

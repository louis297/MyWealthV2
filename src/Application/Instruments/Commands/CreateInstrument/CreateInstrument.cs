using System.Text.RegularExpressions;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Instruments.Commands.CreateInstrument;

[Authorize(Policy = Policies.InstrumentsCreate)]
public class CreateInstrumentCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public required string Symbol { get; init; }

    public required string Name { get; init; }

    public required string QuoteCurrency { get; init; }
}

public class CreateInstrumentCommandValidator : AbstractValidator<CreateInstrumentCommand>
{
    private static readonly Regex SymbolPattern = new("^[A-Z0-9.-]{1,32}$", RegexOptions.CultureInvariant);

    public CreateInstrumentCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.Symbol)
            .Must(symbol =>
            {
                var normalised = symbol?.Trim().ToUpperInvariant();
                return normalised is not null && SymbolPattern.IsMatch(normalised);
            })
            .WithMessage("Symbol must be 1 to 32 characters in [A-Z0-9.-].");
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.QuoteCurrency).NotEmpty();

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class CreateInstrumentCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser)
    : IRequestHandler<CreateInstrumentCommand, Guid>
{
    public async Task<Guid> Handle(CreateInstrumentCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(request.TenantId, cancellationToken);

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create an instrument while the tenant is disabled. Enable the tenant first.");
        }

        var currency = catalog.TryGet(request.QuoteCurrency);
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateInstrumentCommand.QuoteCurrency),
                    "Quote currency must be an enabled catalog code.")
            ]);
        }

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        if (await db.Instruments.AnyAsync(
                row => row.TenantId == tenant.Id && row.Symbol == symbol, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateInstrumentCommand.Symbol),
                    "Symbol must be unique inside the tenant.")
            ]);
        }

        var instrument = Instrument.Create(tenant.Id, request.Symbol, request.Name, currency);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);
        return instrument.PublicId;
    }

    private async Task<Tenant> ResolveTenantAsync(Guid? tenantPublicId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.SingleOrDefaultAsync(
                       row => row.PublicId == tenantPublicId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", tenantPublicId!);
        }

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }
}

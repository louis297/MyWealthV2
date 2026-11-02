using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Portfolios.Commands.CreatePortfolio;

[Authorize(Policy = Policies.PortfoliosManage)]
public class CreatePortfolioCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public string? Name { get; init; }

    public string? BaseCurrency { get; init; }

    public List<PortfolioLineInput>? Lines { get; init; }

    public decimal? DriftAbsolute { get; init; }

    public decimal? DriftRelative { get; init; }

    public decimal? MinimumTrade { get; init; }

    public decimal? CashResidualPercent { get; init; }

    public sealed class PortfolioLineInput
    {
        public Guid InstrumentId { get; init; }

        public decimal TargetWeight { get; init; }
    }
}

public class CreatePortfolioCommandValidator : AbstractValidator<CreatePortfolioCommand>
{
    public CreatePortfolioCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.BaseCurrency).NotEmpty();
        RuleFor(command => command.Lines)
            .NotEmpty()
            .WithMessage("A portfolio needs at least one line.");

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

public class CreatePortfolioCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser)
    : IRequestHandler<CreatePortfolioCommand, Guid>
{
    public async Task<Guid> Handle(CreatePortfolioCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PortfolioTenant.ResolveAsync(db, currentUser, request.TenantId, cancellationToken);
        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a portfolio while the tenant is disabled. Enable the tenant first.");
        }

        var currency = catalog.TryGet(request.BaseCurrency!);
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreatePortfolioCommand.BaseCurrency),
                    "Base currency must be an enabled catalog code.")
            ]);
        }

        var lines = await ResolveLinesAsync(request.Lines!, tenant.Id, cancellationToken);
        Portfolio portfolio;
        try
        {
            portfolio = Portfolio.Create(tenant.Id, request.Name!, currency, lines);
            if (request.DriftAbsolute is not null || request.DriftRelative is not null
                || request.MinimumTrade is not null || request.CashResidualPercent is not null)
            {
                portfolio.ApplySettings(new PortfolioSettings(
                    request.DriftAbsolute ?? portfolio.DriftAbsolute,
                    request.DriftRelative ?? portfolio.DriftRelative,
                    request.MinimumTrade ?? portfolio.MinimumTrade,
                    request.CashResidualPercent ?? portfolio.CashResidualPercent));
            }
        }
        catch (DomainException exception)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreatePortfolioCommand.Lines), exception.Message)]);
        }

        db.Portfolios.Add(portfolio);
        await db.SaveChangesAsync(cancellationToken);
        return portfolio.PublicId;
    }

    private async Task<List<(int InstrumentId, decimal TargetWeight)>> ResolveLinesAsync(
        List<CreatePortfolioCommand.PortfolioLineInput> lines,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var resolved = new List<(int InstrumentId, decimal TargetWeight)>(lines.Count);
        foreach (var line in lines)
        {
            var instrument = await db.Instruments.SingleOrDefaultAsync(
                row => row.PublicId == line.InstrumentId && row.TenantId == tenantId,
                cancellationToken) ?? throw new NotFoundException("Instrument", line.InstrumentId);
            if (!instrument.IsActive)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(CreatePortfolioCommand.Lines), "Instrument is disabled.")
                ]);
            }

            resolved.Add((instrument.Id, line.TargetWeight));
        }

        return resolved;
    }
}

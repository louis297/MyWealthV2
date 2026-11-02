using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.CreatePortfolio;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Portfolios.Commands.UpdatePortfolio;

[Authorize(Policy = Policies.PortfoliosManage)]
public class UpdatePortfolioCommand : IRequest<RebalanceAcceptedDto?>
{
    public Guid Id { get; set; }

    public string? RowVersion { get; set; }

    public string? Name { get; init; }

    public string? BaseCurrency { get; init; }

    public List<CreatePortfolioCommand.PortfolioLineInput>? Lines { get; init; }

    public decimal? DriftAbsolute { get; init; }

    public decimal? DriftRelative { get; init; }

    public decimal? MinimumTrade { get; init; }

    public decimal? CashResidualPercent { get; init; }
}

public class UpdatePortfolioCommandValidator : AbstractValidator<UpdatePortfolioCommand>
{
    public UpdatePortfolioCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Lines).NotEmpty();
    }
}

public class UpdatePortfolioCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IRebalanceScheduler scheduler)
    : IRequestHandler<UpdatePortfolioCommand, RebalanceAcceptedDto?>
{
    public async Task<RebalanceAcceptedDto?> Handle(UpdatePortfolioCommand request, CancellationToken cancellationToken)
    {
        var portfolio = await LoadAsync(request.Id, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(portfolio.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (!string.IsNullOrWhiteSpace(request.BaseCurrency)
            && !string.Equals(request.BaseCurrency.Trim(), portfolio.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(UpdatePortfolioCommand.BaseCurrency), "Base currency cannot change.")
            ]);
        }

        var weightsChanged = false;
        try
        {
            portfolio.Rename(request.Name!);
            if (request.DriftAbsolute is not null || request.DriftRelative is not null
                || request.MinimumTrade is not null || request.CashResidualPercent is not null)
            {
                portfolio.ApplySettings(new PortfolioSettings(
                    request.DriftAbsolute ?? portfolio.DriftAbsolute,
                    request.DriftRelative ?? portfolio.DriftRelative,
                    request.MinimumTrade ?? portfolio.MinimumTrade,
                    request.CashResidualPercent ?? portfolio.CashResidualPercent));
            }

            var previous = portfolio.Lines.ToList();
            portfolio.ReplaceLines(await ResolveLinesAsync(request.Lines!, portfolio.TenantId, cancellationToken));
            weightsChanged = portfolio.DomainEvents.OfType<PortfolioWeightsChanged>().Any();
            if (weightsChanged)
            {
                db.PortfolioLines.RemoveRange(previous);
            }
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(UpdatePortfolioCommand.Lines), exception.Message)
            ]);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (!weightsChanged)
        {
            return null;
        }

        var accountIds = await (
            from assignment in db.AccountPortfolioAssignments
            join account in db.Accounts on assignment.AccountId equals account.Id
            where assignment.PortfolioId == portfolio.Id && assignment.Status == AssignmentStatus.Active
            select account.PublicId).ToListAsync(cancellationToken);
        if (accountIds.Count == 0)
        {
            return null;
        }

        var handles = accountIds
            .Select(accountId => new RebalanceAccountHandleDto(accountId, Guid.NewGuid()))
            .ToList();
        foreach (var handle in handles)
        {
            scheduler.Enqueue(handle.AccountId, handle.Handle);
        }

        return new RebalanceAcceptedDto(Guid.NewGuid(), handles);
    }

    private async Task<Portfolio> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = db.Portfolios.Include(portfolio => portfolio.Lines).Where(portfolio => portfolio.PublicId == id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(portfolio => portfolio.TenantId == currentUser.TenantId);
        }

        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Portfolio", id);
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
                    new ValidationFailure(nameof(UpdatePortfolioCommand.Lines), "Instrument is disabled.")
                ]);
            }

            resolved.Add((instrument.Id, line.TargetWeight));
        }

        return resolved;
    }
}

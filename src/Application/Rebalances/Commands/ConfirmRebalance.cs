using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders.Commands;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Rebalances.Commands;

/// <summary>
/// Rebalances R13: a person confirms a proposal. Under the Account lock the plan is made again against the
/// rebalance's own targets and the first wave of orders is placed; nothing is sent to the broker in the request.
/// </summary>
[Authorize(Policy = Policies.RebalancesConfirm)]
public sealed class ConfirmRebalanceCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }
}

public class ConfirmRebalanceCommandValidator : AbstractValidator<ConfirmRebalanceCommand>
{
    public ConfirmRebalanceCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class ConfirmRebalanceCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ProposeRebalance proposals,
    RebalancePlanning planning,
    PlaceRebalanceWave waves,
    TimeProvider time) : IRequestHandler<ConfirmRebalanceCommand>
{
    public async Task Handle(ConfirmRebalanceCommand request, CancellationToken cancellationToken)
    {
        // R1: a rebalance outside scope, of another tenant, or missing is 404.
        var found = await RebalanceReader.Scope(db, currentUser).AsNoTracking()
                        .Select(row => new { row.Id, row.PublicId, row.AccountId })
                        .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                    ?? throw new NotFoundException("Rebalance", request.Id);
        var person = currentUser.Person?.Id ?? throw new ForbiddenAccessException();

        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                await db.LockAccountAsync(found.AccountId, token);
                var rebalance = await db.Rebalances.Include(row => row.Targets).SingleAsync(row => row.Id == found.Id, token);
                if (!string.Equals(Convert.ToBase64String(rebalance.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
                {
                    throw new ConcurrencyException();
                }

                var now = time.GetUtcNow();
                if (rebalance.Status != RebalanceStatus.Proposed)
                {
                    throw StatusError($"This rebalance is {rebalance.Status} and cannot be confirmed.");
                }

                if (rebalance.IsExpired(now))
                {
                    throw StatusError(Rebalance.ExpiredMessage);
                }

                var account = await db.Accounts.SingleAsync(row => row.Id == rebalance.AccountId, token);
                var portfolio = await db.Portfolios.SingleAsync(row => row.Id == rebalance.PortfolioId, token);
                if (await proposals.EligibilityProblemAsync(account, portfolio, token) is { } problem)
                {
                    throw new RebalanceException(problem.Code, problem.Message);
                }

                var planned = await planning.PlanAsync(
                    account, RebalanceTargets.Of(rebalance), sizeBuysByReservation: true, now, token);
                if (planned.Failed)
                {
                    // R13: the rebalance stays Proposed.
                    throw new RebalanceException(planned.Plan.Code!, planned.Plan.Message!);
                }

                if (planned.Plan.Trades.Count == 0)
                {
                    rebalance.EndNoChange(now);
                }
                else
                {
                    var connection = await db.Tenants
                        .Where(row => row.Id == account.TenantId)
                        .Select(row => row.BrokerConnection)
                        .SingleAsync(token);
                    rebalance.Confirm(person, connection!, now);
                    if ((await waves.PlaceAsync(rebalance, account, planned, now, token)).Count == 0)
                    {
                        rebalance.EndNoChange(now);
                    }
                }

                await db.SaveChangesAsync(token);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
        catch (RebalanceStatusException exception)
        {
            throw StatusError(exception.Message);
        }
    }

    private static ValidationException StatusError(string message) =>
        new([new ValidationFailure("Status", message)]);
}

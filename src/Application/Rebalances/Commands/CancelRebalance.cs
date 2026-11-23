using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders.Commands;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Rebalances;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Rebalances.Commands;

/// <summary>Rebalances R19: a person cancels a rebalance. What the broker has filled stays.</summary>
[Authorize(Policy = Policies.RebalancesConfirm)]
public sealed class CancelRebalanceCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }
}

public class CancelRebalanceCommandValidator : AbstractValidator<CancelRebalanceCommand>
{
    public CancelRebalanceCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note is null || note.Trim().Length is >= 1 and <= RebalanceRules.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class CancelRebalanceCommandHandler(
    IApplicationDbContext db, ICurrentUser currentUser, ProposeRebalance proposals, TimeProvider time)
    : IRequestHandler<CancelRebalanceCommand>
{
    public async Task Handle(CancelRebalanceCommand request, CancellationToken cancellationToken)
    {
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
                var rebalance = await db.Rebalances.SingleAsync(row => row.Id == found.Id, token);
                if (!string.Equals(Convert.ToBase64String(rebalance.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
                {
                    throw new ConcurrencyException();
                }

                if (!rebalance.IsOpen)
                {
                    throw StatusError($"This rebalance is {rebalance.Status} and cannot be cancelled.");
                }

                if (rebalance.HasCancelRequest)
                {
                    throw StatusError(Rebalance.CancelAlreadyRequestedMessage);
                }

                await RebalanceCancellation.CancelAsync(db, rebalance, person, request.Note, time.GetUtcNow(), token);
                await db.SaveChangesAsync(token);
                // R12: a rebalance cancelled in the request may owe a follow-up.
                await proposals.FollowUpAsync(rebalance, token);
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

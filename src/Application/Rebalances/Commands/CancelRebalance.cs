using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders.Commands;
using MyWealthV2.Domain.Rebalances;

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

public class CancelRebalanceCommandHandler : IRequestHandler<CancelRebalanceCommand>
{
    public Task Handle(CancelRebalanceCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

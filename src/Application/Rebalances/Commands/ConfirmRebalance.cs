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

public class ConfirmRebalanceCommandHandler : IRequestHandler<ConfirmRebalanceCommand>
{
    public Task Handle(ConfirmRebalanceCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

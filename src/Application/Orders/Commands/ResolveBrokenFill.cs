using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Orders.Commands;

/// <summary>Trade-settlement R31: a TenantAdmin who corrected the account with an adjustment marks a break that
/// could not be applied as resolved.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class ResolveBrokenFillCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public Guid FillId { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class ResolveBrokenFillCommandHandler : IRequestHandler<ResolveBrokenFillCommand>
{
    public Task Handle(ResolveBrokenFillCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

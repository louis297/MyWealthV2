using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R15, R24: a SystemAdmin runs the valuations of a Tenant through a date; it runs at once.</summary>
[Authorize(Policy = Policies.ValuationRunsStart)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class StartValuationRunCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public string? ThroughDate { get; init; }
}

public class StartValuationRunCommandHandler : IRequestHandler<StartValuationRunCommand, Guid>
{
    public const string NotFinalMessage = "A valuation can be stored only for a day whose prices and rates are final.";

    public Task<Guid> Handle(StartValuationRunCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

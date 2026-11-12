using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace Microsoft.Extensions.Hosting;

// Marks an endpoint that only matches requests arriving on the given local port.
public sealed record HealthPortMetadata(int Port);

// Drops endpoints with HealthPortMetadata when the connection's local port differs, so the request
// falls through to the host's normal routing (production-hosting R12). The local port is used, not
// the Host header: the reverse proxy passes the client's Host through unchanged.
internal sealed class HealthPortMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) =>
        endpoints.Any(endpoint => endpoint.Metadata.GetMetadata<HealthPortMetadata>() is not null);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!candidates.IsValidCandidate(i))
            {
                continue;
            }

            var metadata = candidates[i].Endpoint.Metadata.GetMetadata<HealthPortMetadata>();
            if (metadata is not null && httpContext.Connection.LocalPort != metadata.Port)
            {
                candidates.SetValidity(i, false);
            }
        }

        return Task.CompletedTask;
    }
}

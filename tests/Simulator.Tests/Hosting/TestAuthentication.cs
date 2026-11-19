using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Simulator.Tests.Hosting;

/// <summary>
/// Stands in for the simulator's session cookie (simulator spec §10: control sign-in uses a test authentication
/// handler; the OIDC exchange itself is covered by the identity functional tests). A request with
/// <see cref="EmailHeader"/> is signed in as that person, with the role in <see cref="RoleHeader"/>
/// (default <c>simulatorOperator</c>). Challenge and forbid stay with the host's own schemes.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string EmailHeader = "X-Test-Email";
    public const string RoleHeader = "X-Test-Role";
    public const string OperatorRole = "simulatorOperator";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(EmailHeader, out var email))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = Request.Headers.TryGetValue(RoleHeader, out var value) ? value.ToString() : OperatorRole;
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", email.ToString()),
                new Claim("name", "Test Person"),
                new Claim("role", role)
            ],
            SchemeName,
            "name",
            "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

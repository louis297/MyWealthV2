using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace MyWealthV2.Simulator.Control;

/// <summary>Simulator spec R16–R19: control sign-in. Stub.</summary>
public static class SimulatorAuthentication
{
    public const string SessionCookieName = "__Host-simulator";

    public static Task RejectOtherRolesAsync(TokenValidatedContext context) => Task.CompletedTask;
}

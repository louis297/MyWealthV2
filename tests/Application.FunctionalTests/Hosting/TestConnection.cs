using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// TestServer has no socket, so RemoteIpAddress is null, which ForwardedHeadersMiddleware treats as trusted.
// This filter runs before the host's own middleware and gives each request a connection address like Kestrel would:
// loopback by default, or the address in X-Test-Remote-Ip. X-Test-Local-Port sets the port the request arrived on.
internal sealed class TestConnection : IStartupFilter
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";
    public const string LocalPortHeader = "X-Test-Local-Port";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress =
                context.Request.Headers.TryGetValue(RemoteIpHeader, out var remote)
                    ? IPAddress.Parse(remote.ToString())
                    : context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
            if (context.Request.Headers.TryGetValue(LocalPortHeader, out var port))
            {
                context.Connection.LocalPort = int.Parse(port.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            }

            await nextMiddleware(context);
        });
        next(app);
    };
}

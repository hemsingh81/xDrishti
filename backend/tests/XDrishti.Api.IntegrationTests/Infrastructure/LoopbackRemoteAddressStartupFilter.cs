using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The in-memory test server has no client address. Give every request the loopback address (a trusted proxy),
/// so the forwarded-header handling of the real pipeline applies exactly as behind Caddy.
/// </summary>
internal sealed class LoopbackRemoteAddressStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}

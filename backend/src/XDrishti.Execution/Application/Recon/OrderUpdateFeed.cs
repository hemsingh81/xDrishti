using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

/// <summary>
/// The text channel of the order-update stream. A real implementation is a later slice; in P12-06 only an in-process fake exists, so nothing here can open a network connection. <see cref="ReceiveAsync"/> returns null when the peer closed.
/// </summary>
public interface IOrderUpdateTransport
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(string text, CancellationToken cancellationToken);

    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    Task CloseAsync();
}

/// <summary>
/// Which endpoint the stream may connect to. Dhan documents one host for the order-update stream (the live one) and no sandbox one; this class never names it. For environment sandbox it returns an address only when the configured host is exactly the pinned sandbox host
/// (<see cref="Dhan.ExecutionEndpoints.SandboxHost"/>, compared without regard to case, nothing else accepted: no port, user, path, trailing dot or look-alike); for live and none it returns nothing. Without an endpoint the stream does not run and the poll alone carries the system.
/// </summary>
public static class StreamEndpoint
{
    public static Uri? Resolve(ExecEnvironment environment, string? sandboxHost) =>
        environment == ExecEnvironment.Sandbox && sandboxHost is not null && string.Equals(sandboxHost, Dhan.ExecutionEndpoints.SandboxHost, StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder { Scheme = "wss", Host = Dhan.ExecutionEndpoints.SandboxHost, Port = -1 }.Uri
            : null;
}

/// <summary>How one connection ended.</summary>
public enum StreamEnd
{
    Closed,
    NoEndpoint,
    LoginFailed,
    Cancelled,
}

/// <summary>
/// The client of the order-update stream (spec P12-06): connect, send the login message, then read messages until the peer closes. Each message is parsed tolerantly and applied once by <see cref="UpdateApplier"/>; an unreadable one is dropped, counted, and a poll is asked for. Silence is not an
/// error. It never writes anything but the login message. The login text comes from a delegate so the token stays out of this class and out of every log.
/// </summary>
public sealed class OrderUpdateFeed(IOrderUpdateTransport transport, Uri? endpoint, Func<string> login, UpdateApplier applier, StreamStatus status, UpdateCounters counters)
{
    public async Task<StreamEnd> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (endpoint is null)
        {
            return StreamEnd.NoEndpoint;
        }

        try
        {
            await transport.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            await transport.SendAsync(login(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return StreamEnd.Cancelled;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status.Disconnected();
            return StreamEnd.LoginFailed;
        }

        status.Connected();
        try
        {
            while (await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } text)
            {
                status.Message();
                var parsed = OrderUpdateParser.Parse(text);
                switch (parsed.Kind)
                {
                    case UpdateParseKind.Update:
                        applier.Apply(parsed.Update!);
                        break;
                    case UpdateParseKind.Unreadable:
                        counters.Drop();
                        status.RequestPoll();
                        break;
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            status.Disconnected();
            return StreamEnd.Cancelled;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // a dropped connection: the poll covers the gap, the caller reconnects
        }

        status.Disconnected();
        await transport.CloseAsync().ConfigureAwait(false);
        return StreamEnd.Closed;
    }
}

using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

/// <summary>Defence in depth: every request that reaches the wire is checked against <see cref="ExecutionEndpoints"/>. A request that does not pass never reaches the inner handler.</summary>
public sealed class GuardHandler(ExecEnvironment environment, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!ExecutionEndpoints.IsAllowed(environment, request.Method, request.RequestUri))
        {
            throw new HttpRequestException("The request is not on the execution guard table.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

namespace XDrishti.Infrastructure.Market;

/// <summary>A master-data HTTP client was asked to call an endpoint outside the allow-list (a programming error). The message names method, host and path only.</summary>
public sealed class MasterDataEndpointNotAllowedException : InvalidOperationException
{
    public MasterDataEndpointNotAllowedException()
        : this("A master-data endpoint outside the allow-list was requested.")
    {
    }

    public MasterDataEndpointNotAllowedException(string message)
        : base(message)
    {
    }

    public MasterDataEndpointNotAllowedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal static MasterDataEndpointNotAllowedException For(HttpRequestMessage request) =>
        new($"The master-data endpoint {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath} is not on the allow-list.");
}

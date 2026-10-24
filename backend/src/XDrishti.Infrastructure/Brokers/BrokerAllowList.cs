using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>The broker allow-list for the phase check (ADR 0097): reads <see cref="BrokerEndpoints"/>. It sends nothing and changes nothing.</summary>
internal sealed class BrokerAllowList : IBrokerAllowList
{
    public IReadOnlyList<string> Entries() => BrokerEndpoints.Describe();

    public bool Allows(string method, string url) =>
        !string.IsNullOrWhiteSpace(method) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && BrokerEndpoints.IsAllowed(new HttpMethod(method), uri);
}

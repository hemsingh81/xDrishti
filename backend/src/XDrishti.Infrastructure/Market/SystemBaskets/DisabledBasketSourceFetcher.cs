using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>The fetcher registered while <c>Baskets:System:Fetch:Enabled</c> is off: no HTTP client exists and it never reaches the network (the handler refuses before it anyway).</summary>
internal sealed class DisabledBasketSourceFetcher : IBasketSourceFetcher
{
    public ValueTask<SourceResponse> GetAsync(BasketSourceInfo source, CancellationToken cancellationToken) => ValueTask.FromResult(SourceResponse.Unreachable("disabled"));
}

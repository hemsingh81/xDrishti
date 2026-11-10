namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>The one port that reaches the network for the system baskets: a conditional GET of a catalogue file (If-None-Match / If-Modified-Since from the source's stored validators). Never throws for a network problem.</summary>
public interface IBasketSourceFetcher
{
    ValueTask<SourceResponse> GetAsync(BasketSourceInfo source, CancellationToken cancellationToken);
}

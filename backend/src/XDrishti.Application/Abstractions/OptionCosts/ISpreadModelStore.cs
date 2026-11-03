using XDrishti.Domain.Options.Costs;

namespace XDrishti.Application.Abstractions.OptionCosts;

/// <summary>The stored half-spread models (<c>research.option_spread_models</c>, <c>research.option_spread_buckets</c>), insert-only and content-addressed: the id is the SHA-256 of the inputs, so storing the same model twice writes nothing.</summary>
public interface ISpreadModelStore
{
    /// <summary>Whether a model with the id is stored.</summary>
    ValueTask<bool> ExistsAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>Stores the model and its buckets in one transaction; false (nothing written) when the id is stored already.</summary>
    ValueTask<bool> AppendAsync(BuiltSpreadModel model, CancellationToken cancellationToken);

    /// <summary>The newest model of the underlying (by last session, then build time), or null when none is stored.</summary>
    ValueTask<HalfSpreadModel?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken);
}

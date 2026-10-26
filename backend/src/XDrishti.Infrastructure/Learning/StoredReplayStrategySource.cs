using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.Learning;

/// <summary>The stored text of a strategy version by its hash, for a replay (spec P8-18 Part B). Reads only: it has no way to store or change a strategy.</summary>
internal sealed class StoredReplayStrategySource(IStrategyStore strategies) : IReplayStrategySource
{
    public async ValueTask<string?> StoredSourceAsync(string hash, CancellationToken cancellationToken) =>
        (await strategies.GetByHashAsync(hash, cancellationToken))?.SourceText;
}

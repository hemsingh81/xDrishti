using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>Reads the active set: every provider proposes, the builder merges, orders and cuts (ADR 0024 §2). A cut is warned about once, again only when the number cut changes (not every minute).</summary>
internal sealed class ActiveSetLoader(IServiceScopeFactory scopes, LiveFeedSettings settings, ILogger<ActiveSetLoader> logger)
{
    private int _lastWarnedCut;

    public async ValueTask<ActiveSet> LoadAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<ActiveSetCandidate>();
        await using (var scope = scopes.CreateAsyncScope())
        {
            foreach (var provider in scope.ServiceProvider.GetServices<IActiveSetProvider>())
            {
                candidates.AddRange(await provider.ListAsync(cancellationToken));
            }
        }

        var set = ActiveSetBuilder.Build(candidates, settings.MaxInstruments);
        if (set.Cut != _lastWarnedCut)
        {
            _lastWarnedCut = set.Cut;
            if (set.Cut > 0)
            {
                LiveFeedLog.SetCut(logger, set.Cut, settings.MaxInstruments);
            }
        }

        return set;
    }
}

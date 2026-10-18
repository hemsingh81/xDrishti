using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Research;

namespace XDrishti.Api.Simulate;

/// <summary>
/// Resolves the universe of a verb run through the existing queries, strictly (spec P3-04b2, D4). Nothing is guessed: an unmatched symbol refuses the run and every unmatched entry is listed with its reason. For the tick gate (D7) it also reads
/// each instrument's stored tick and type, one small query per instrument or one basket read, and never a bar. The order of the instruments is the order given.
/// </summary>
internal static class UniverseResolver
{
    /// <summary>The most manual basket names listed when a name matches nothing.</summary>
    public const int MaxBasketNames = 20;

    public static async Task<SimulateUniverse> ResolveAsync(SimulateOptions options, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        return options.Basket is { } basket
            ? await BasketAsync(basket, services, cancellationToken)
            : await SymbolsAsync(options.Symbols, services, cancellationToken);
    }

    private static async Task<SimulateUniverse> SymbolsAsync(IReadOnlyList<string> symbols, IServiceProvider services, CancellationToken cancellationToken)
    {
        var resolve = services.GetRequiredService<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>();
        var resolved = await resolve.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput(symbols)), cancellationToken);
        if (resolved.IsFailure)
        {
            return SimulateUniverse.Refused([resolved.Error.Message]);
        }

        var result = resolved.Value;
        if (result.Unmatched.Count > 0)
        {
            return SimulateUniverse.Refused([.. result.Unmatched.Select(u => "symbol '" + SimulateOptions.Show(u.Term) + "': " + u.Reason)]);
        }

        var details = services.GetRequiredService<IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>>();
        var probes = new List<TickProbe>(result.Matched.Count);
        var notChecked = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var match in result.Matched)
        {
            var detail = await details.HandleAsync(new GetInstrumentQuery(match.Id), cancellationToken);
            if (detail.IsFailure)
            {
                continue; // the instrument vanished between the two reads: the run skips it as instrument_not_found
            }

            probes.Add(Probe(match.Id, match.Symbol, detail.Value.Type, detail.Value.TickSize, notChecked));
        }

        return new SimulateUniverse(
            [.. result.Matched.Select(m => m.Id)], null, probes, [.. result.Matched.Where(m => m.Retired).Select(m => m.Symbol)], result.Duplicates, notChecked, []);
    }

    private static async Task<SimulateUniverse> BasketAsync(string basket, IServiceProvider services, CancellationToken cancellationToken)
    {
        var list = services.GetRequiredService<IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>>();
        var manual = (await list.HandleAsync(new ListBasketsQuery(false), cancellationToken))
            .Where(b => !b.System && string.Equals(b.Kind, BasketKinds.Manual, StringComparison.Ordinal))
            .ToList();

        Guid id;
        if (Guid.TryParse(basket, out var guid))
        {
            // The handler is the judge of a portfolio, archived or unknown basket (simulation.basket_not_found); the verb only reads the members for the tick gate when it is a manual one.
            id = guid;
            if (manual.All(b => b.Id != guid))
            {
                return new SimulateUniverse(null, guid, [], [], 0, new Dictionary<string, int>(), []);
            }
        }
        else
        {
            var match = manual.FirstOrDefault(b => string.Equals(b.Name, basket, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return SimulateUniverse.Refused([NoBasket(basket, manual)]);
            }

            id = match.Id;
        }

        var members = services.GetRequiredService<IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>>>();
        var read = await members.HandleAsync(new GetBasketMembersQuery(id), cancellationToken);
        var probes = new List<TickProbe>();
        var retired = new List<string>();
        var notChecked = new Dictionary<string, int>(StringComparer.Ordinal);
        if (read.IsSuccess)
        {
            foreach (var member in read.Value.Items.OrderBy(m => m.Position))
            {
                probes.Add(Probe(member.Instrument.Id, member.Instrument.Symbol, member.Instrument.Type, member.Instrument.TickSize, notChecked));
                if (member.Instrument.Retired)
                {
                    retired.Add(member.Instrument.Symbol);
                }
            }
        }

        return new SimulateUniverse(null, id, probes, retired, 0, notChecked, []);
    }

    private static string NoBasket(string basket, List<BasketView> manual)
    {
        var names = manual.Select(b => b.Name).Order(StringComparer.OrdinalIgnoreCase).Take(MaxBasketNames).ToList();
        var more = manual.Count - names.Count;
        var listed = names.Count == 0 ? "there are none" : string.Join(", ", names) + (more > 0 ? " and " + more + " more" : string.Empty);
        return "basket '" + SimulateOptions.Show(basket) + "' matches no active manual basket by name or id; the manual baskets are: " + listed;
    }

    /// <summary>One probe for the tick check; a type that is not a share or an ETF is counted for the summary's note (the check does not gate it), an unknown type code too.</summary>
    private static TickProbe Probe(Guid id, string symbol, string typeCode, decimal? storedTick, Dictionary<string, int> notChecked)
    {
        if (!InstrumentTypeCodes.TryParse(typeCode, out var type))
        {
            notChecked[SimulateUniverse.UnknownType] = notChecked.GetValueOrDefault(SimulateUniverse.UnknownType) + 1;
            return new TickProbe(id, symbol, default, null);
        }

        if (type is not (InstrumentType.Share or InstrumentType.Etf))
        {
            notChecked[typeCode] = notChecked.GetValueOrDefault(typeCode) + 1;
        }

        return new TickProbe(id, symbol, type, storedTick);
    }
}

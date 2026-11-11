using System.Globalization;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Reads a basket's members over a window through the existing ports (BK-07 Decision 6): the members at the start, the history of additions and removals replayed day by day (members at the end of a day, as the members read says), and the members at the stated date.
/// With no more than one day of change the members are those of one date and the basis says so; before the first known date the first known members apply (ISS-22).
/// </summary>
internal sealed class BasketMembershipLoader(IBasketQueries baskets, IPortfolioBasketSource portfolio, IDerivedVersionStore derived, IHoldingClassifications classifications, BasketProfileSettings settings, TimeProvider time)
{
    private const int HistoryTake = 20_000;

    public async ValueTask<Result<BasketMembership>> LoadAsync(Guid id, ResolvedWindow window, string? membersAsOfText, CancellationToken cancellationToken)
    {
        DateOnly? asked = null;
        if (!string.IsNullOrWhiteSpace(membersAsOfText))
        {
            if (!DateOnly.TryParseExact(membersAsOfText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 2000 or > 2999)
            {
                return Result.Failure<BasketMembership>(ReportErrors.Invalid("membersAsOf must be a date such as 2026-10-11."));
            }

            asked = date;
        }

        var view = await baskets.GetAsync(id, cancellationToken);
        if (view is null)
        {
            return await PortfolioAsync(id, asked, cancellationToken);
        }

        if (view.ArchivedAt is not null)
        {
            return Result.Failure<BasketMembership>(NotFound(id));
        }

        var asOf = asked ?? window.To;
        var start = await baskets.MembersAsync(id, BasketMember.CutoffAfter(window.From), window.From, cancellationToken);
        var end = await baskets.MembersAsync(id, BasketMember.CutoffAfter(asOf), asOf, cancellationToken);
        var history = await baskets.HistoryAsync(id, HistoryTake, cancellationToken);
        if (start is null || end is null || history is null)
        {
            return Result.Failure<BasketMembership>(NotFound(id));
        }

        var events = history.Items.OrderBy(e => e.At).ThenBy(e => e.Kind == "removed" ? 0 : 1).ToList();
        var dates = events.Select(e => IndiaTime.Today(e.At)).Distinct().Order().ToList();
        // dated = more than one day of change and some of it inside the window; a creation on one day, or changes all before the window, leave one set over the whole window (a single snapshot)
        var dated = dates.Count >= 2 && events.Any(e => IndiaTime.Today(e.At) > window.From && IndiaTime.Today(e.At) <= window.To);
        var refs = new Dictionary<string, MemberRef>(StringComparer.Ordinal);
        foreach (var m in start.Items.Concat(end.Items))
        {
            refs.TryAdd(m.Instrument.Symbol, new MemberRef(m.Instrument.Id, m.Instrument.Symbol, m.Instrument.Sector, m.Instrument.FnoEligible, m.Instrument.AsmGsm));
        }

        Func<DateOnly, IReadOnlySet<string>> at;
        if (dated)
        {
            var current = start.Items.Select(m => m.Instrument.Symbol).ToHashSet(StringComparer.Ordinal);
            var steps = new List<(DateOnly Date, IReadOnlySet<string> Set)>();
            foreach (var group in events.Where(e => IndiaTime.Today(e.At) > window.From && IndiaTime.Today(e.At) <= window.To).GroupBy(e => IndiaTime.Today(e.At)))
            {
                foreach (var e in group)
                {
                    if (e.Kind == "added")
                    {
                        current.Add(e.Symbol);
                        refs.TryAdd(e.Symbol, new MemberRef(e.InstrumentId, e.Symbol, null, false, false, false));
                    }
                    else
                    {
                        current.Remove(e.Symbol);
                    }
                }

                steps.Add((group.Key, current.ToHashSet(StringComparer.Ordinal)));
            }

            IReadOnlySet<string> first = start.Items.Select(m => m.Instrument.Symbol).ToHashSet(StringComparer.Ordinal);
            at = day =>
            {
                var set = first;
                foreach (var step in steps)
                {
                    if (step.Date > day)
                    {
                        break;
                    }

                    set = step.Set;
                }

                return set;
            };
        }
        else
        {
            var constant = end.Items.Select(m => m.Instrument.Symbol).ToHashSet(StringComparer.Ordinal);
            at = _ => constant;
            refs = refs.Where(kv => constant.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        var noSector = refs.Values.Where(r => r.Sector is null && !r.FlagsKnown).Select(r => r.Id).ToList();
        if (noSector.Count > 0)
        {
            var sectors = await classifications.SectorsAsync(noSector, cancellationToken);
            foreach (var r in refs.Values.Where(r => !r.FlagsKnown && sectors.ContainsKey(r.Id)).ToList())
            {
                refs[r.Symbol] = r with { Sector = sectors[r.Id] };
            }
        }

        if (refs.Count > settings.MaxInstruments)
        {
            return Result.Failure<BasketMembership>(ReportErrors.Invalid($"Basket '{view.Name}' holds {refs.Count} instruments; at most {settings.MaxInstruments} are read."));
        }

        string? hash = null;
        if (view.Kind == BasketKinds.Derived && await derived.ListVersionsAsync(id, 0, 1, cancellationToken) is { Items: [var newest, ..] })
        {
            hash = newest.ParamsHash;
        }

        var version = $"{events.Count}:{(events.Count == 0 ? 0 : events[^1].At.UtcTicks)}";
        return Result.Success(new BasketMembership(id, view.Name, view.Kind, hash, [.. refs.Values.OrderBy(r => r.Symbol, StringComparer.Ordinal)], at, dated ? dates : [.. dates.Take(1)], asOf, dated ? MemberTurnover.Dated : MemberTurnover.SingleSnapshot, version));
    }

    private async ValueTask<Result<BasketMembership>> PortfolioAsync(Guid id, DateOnly? asked, CancellationToken cancellationToken)
    {
        if (!await portfolio.ExistsAsync(id, cancellationToken))
        {
            return Result.Failure<BasketMembership>(NotFound(id));
        }

        if (asked is not null)
        {
            return Result.Failure<BasketMembership>(ReportErrors.Invalid("A portfolio basket has no past members: it shows the current holdings."));
        }

        var holdings = await portfolio.MembersAsync(id, cancellationToken);
        if (holdings is null)
        {
            return Result.Failure<BasketMembership>(NotFound(id));
        }

        var refs = holdings.Items.GroupBy(m => m.Instrument.Symbol, StringComparer.Ordinal).Select(g => g.First().Instrument)
            .Select(i => new MemberRef(i.Id, i.Symbol, i.Sector, i.FnoEligible, i.AsmGsm)).OrderBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        if (refs.Count > settings.MaxInstruments)
        {
            return Result.Failure<BasketMembership>(ReportErrors.Invalid($"Basket '{holdings.Name}' holds {refs.Count} instruments; at most {settings.MaxInstruments} are read."));
        }

        IReadOnlySet<string> set = refs.Select(r => r.Symbol).ToHashSet(StringComparer.Ordinal);
        var today = IndiaTime.Today(time.GetUtcNow());
        return Result.Success(new BasketMembership(id, holdings.Name, BasketKinds.Portfolio, null, refs, _ => set, [today], today, MemberTurnover.SingleSnapshot, Fingerprint(holdings)));
    }

    /// <summary>A hash of the sorted instrument ids and quantities: a trade that changes a holding changes the cache key.</summary>
    private static string Fingerprint(BasketMembers holdings)
    {
        var text = string.Join(';', holdings.Items.OrderBy(m => m.Instrument.Id).Select(m => $"{m.Instrument.Id:N}:{m.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
        return "holdings:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static Error NotFound(Guid id) => Error.NotFound("reports.not_found", $"Basket '{id:D}' does not exist or is archived.");
}

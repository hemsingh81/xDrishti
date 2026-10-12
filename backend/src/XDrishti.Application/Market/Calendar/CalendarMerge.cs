using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>What a merge did to the calendar rows.</summary>
internal readonly record struct MergeCounts(int Added, int Updated, int Removed, int Unchanged, int Skipped)
{
    public bool Changed => Added + Updated + Removed > 0;
}

/// <summary>
/// How the sources of the calendar meet (ADR 0017 §1, in order of trust: manual, observed, NSE). The owner's rows are never touched; a day the owner dismissed is not
/// proposed or listed again. Where the stored bars (observed) contradict NSE's confirmed list, the NSE row keeps deciding what a session is and the contradiction is attached to
/// it as a proposal for the owner (a session seen on a listed holiday must not turn the date into an ordinary weekday before the owner decided); an agreement makes the NSE row the
/// confirmed one. Rows are changed through the store's tracking; the caller saves.
/// </summary>
internal static class CalendarMerge
{
    /// <summary>Applies what the stored bars suggest: proposes new days, refreshes the proposals it made before and withdraws those the bars no longer support.</summary>
    public static MergeCounts ApplyObserved(IReadOnlyList<ObservedProposal> proposals, IReadOnlyList<TradingDay> existing, ITradingDayStore store, DateTimeOffset now)
    {
        var byDate = existing.ToDictionary(d => d.Id);
        var proposed = proposals.Select(p => p.Date).ToHashSet();
        int added = 0, updated = 0, removed = 0, unchanged = 0, skipped = 0;
        foreach (var proposal in proposals)
        {
            var window = proposal.FromMinute is { } from && proposal.ToMinute is { } to ? (from, to) : ((int, int)?)null;
            if (!byDate.TryGetValue(proposal.Date, out var current))
            {
                store.Add(Create(proposal, window, now));
                added++;
            }
            else if (current.Status == CalendarStatus.Dismissed || current.Source == CalendarSource.Manual || (current.Source == CalendarSource.Observed && current.IsConfirmed))
            {
                skipped++;
            }
            else if (current.IsConfirmed)
            {
                // NSE's confirmed row stays in force. What the bars show is a proposal next to it, or nothing when they agree.
                if (current.Kind == proposal.Kind && current.Window == window)
                {
                    if (current.ClearProposal(now))
                    {
                        removed++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                else if (current.Propose(proposal.Kind, window, proposal.Note, now))
                {
                    updated++;
                }
                else if (current.ProposalStatus == CalendarStatus.Dismissed)
                {
                    skipped++;
                }
                else
                {
                    unchanged++;
                }
            }
            else if (current.Replace(proposal.Kind, window, CalendarSource.Observed, CalendarStatus.Proposed, proposal.Note, now))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        // Withdrawing a proposal deletes its row (or clears it from the confirmed row). The save is refused when the row changed since it was read, so a proposal the owner
        // confirmed in the meantime is never deleted.
        foreach (var stale in existing.Where(d => d.Source == CalendarSource.Observed && d.Status == CalendarStatus.Proposed && !proposed.Contains(d.Id)))
        {
            store.Remove(stale);
            removed++;
        }

        foreach (var stale in existing.Where(d => d.HasPendingProposal && !proposed.Contains(d.Id)))
        {
            if (stale.ClearProposal(now))
            {
                removed++;
            }
        }

        return new MergeCounts(added, updated, removed, unchanged, skipped);
    }

    /// <summary>Applies NSE's holidays: adds the ones that are not on the calendar (confirmed), keeps what the owner or the stored bars decided.</summary>
    public static MergeCounts ApplyNse(IEnumerable<ListedHoliday> holidays, IReadOnlyList<TradingDay> existing, ITradingDayStore store, DateTimeOffset now)
    {
        var byDate = existing.ToDictionary(d => d.Id);
        int added = 0, updated = 0, unchanged = 0, skipped = 0;
        foreach (var holiday in holidays)
        {
            var note = string.IsNullOrWhiteSpace(holiday.Description) ? "Listed by NSE" : holiday.Description;
            if (!byDate.TryGetValue(holiday.Date, out var current))
            {
                store.Add(TradingDay.Holiday(holiday.Date, CalendarSource.Nse, CalendarStatus.Confirmed, note, now));
                added++;
            }
            else if (current.Status == CalendarStatus.Dismissed || current.Source == CalendarSource.Manual || current.Kind != CalendarDayKind.Holiday
                     || (current.Source == CalendarSource.Observed && current.IsConfirmed))
            {
                skipped++;
            }
            else if (current.Replace(CalendarDayKind.Holiday, null, CalendarSource.Nse, CalendarStatus.Confirmed, note, now))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        return new MergeCounts(added, updated, 0, unchanged, skipped);
    }

    private static TradingDay Create(ObservedProposal proposal, (int From, int To)? window, DateTimeOffset now) =>
        proposal.Kind == CalendarDayKind.Special && window is { } w
            ? TradingDay.Special(proposal.Date, w.From, w.To, CalendarSource.Observed, CalendarStatus.Proposed, proposal.Note, now)
            : TradingDay.Holiday(proposal.Date, CalendarSource.Observed, CalendarStatus.Proposed, proposal.Note, now);
}

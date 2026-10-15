using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Saves the aliases the owner asked to remember (ADR 0028; also the choices of a report import, ADR 0029): a new one, or the existing one for the name pointed elsewhere; the number of aliases that changed. The existing ones are read in one query, and one audit event says
/// how many were created and pointed elsewhere (counts only). Nothing is saved here: the caller's unit of work does.
/// </summary>
internal static class DividendAliasSaver
{
    public static async ValueTask<int> SaveAsync(
        IDividendAliases aliases, IAccountEventLog events, Guid accountId, IReadOnlyList<AliasToSave> toSave, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(toSave);
        if (toSave.Count == 0)
        {
            return 0;
        }

        var existing = (await aliases.FindByKeysAsync(accountId, [.. toSave.Select(s => s.NameKey)], cancellationToken)).ToDictionary(a => a.NameKey, StringComparer.Ordinal);
        var created = 0;
        var pointedElsewhere = 0;
        foreach (var save in toSave)
        {
            if (existing.TryGetValue(save.NameKey, out var current))
            {
                if (current.Retarget(save.HoldingId, save.InstrumentId, at))
                {
                    pointedElsewhere++;
                }

                continue;
            }

            aliases.Add(DividendAlias.Create(accountId, save.NameKey, save.HoldingId, save.InstrumentId, actor, at));
            created++;
        }

        if (created + pointedElsewhere > 0)
        {
            events.Record(AccountEvent.Record(AccountEventType.DividendAliasSaved, accountId, $"Dividend aliases saved: {created} new, {pointedElsewhere} pointed elsewhere", actor, at));
        }

        return created + pointedElsewhere;
    }
}

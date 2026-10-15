using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A name and ISIN a file teaches, with the name as written.</summary>
internal sealed record TaughtName(string NameKey, string Isin, string Name);

/// <summary>
/// The names that files teach the application belong to ISINs (ADR 0029): the names of a demat holding statement or a depository-charges sheet with their ISINs, and the ISINs the owner typed when choosing. <see cref="Of"/> reduces the (name, ISIN) pairs of a file to the distinct
/// ones; <see cref="Missing"/> leaves out those remembered already; <see cref="Learn"/> collects the new aliases (the caller's unit of work saves them) and audits how many, counts only.
/// </summary>
internal static class SecurityNames
{
    public static IReadOnlyList<TaughtName> Of(IEnumerable<(string Name, string Isin)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var seen = new HashSet<(string, string)>();
        var taught = new List<TaughtName>();
        foreach (var (name, isin) in pairs)
        {
            var key = DividendNames.Key(name);
            if (key.Length > 0 && key.Length <= SecurityAlias.MaxNameKeyLength && seen.Add((key, isin)))
            {
                taught.Add(new TaughtName(key, isin, name));
            }
        }

        return taught;
    }

    public static IReadOnlyList<TaughtName> Missing(IReadOnlyList<SecurityAlias> remembered, IEnumerable<TaughtName> taught)
    {
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(taught);
        var known = remembered.Select(a => (a.NameKey, a.Isin)).ToHashSet();
        return [.. taught.Where(t => !known.Contains((t.NameKey, t.Isin)))];
    }

    public static int Learn(ISecurityAliases store, IAccountEventLog events, Guid accountId, IReadOnlyList<TaughtName> missing, SecurityAliasSource source, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(missing);
        foreach (var name in missing)
        {
            store.Add(SecurityAlias.Create(accountId, name.NameKey, name.Isin, name.Name, source, actor, at));
        }

        if (missing.Count > 0)
        {
            events.Record(AccountEvent.Record(AccountEventType.SecurityNamesLearned, accountId, $"Security names learned from {Origin(source)}: {missing.Count} new", actor, at));
        }

        return missing.Count;
    }

    private static string Origin(SecurityAliasSource source) => source switch
    {
        SecurityAliasSource.Demat => "a demat holding statement",
        SecurityAliasSource.Dp => "a depository-charges sheet",
        _ => "the owner's choice",
    };
}

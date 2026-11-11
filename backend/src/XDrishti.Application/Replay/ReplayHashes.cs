using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>
/// The two hashes of a replay (SR-01, AC 13 and 16). The <b>input hash</b> covers everything a run reads: the parameters, the settings snapshot, the scope hash, the strategy version hashes and the bar hash. The <b>result hash</b> covers the session records, in session order.
/// Two runs with the same input hash give the same result hash; a run that was stopped and resumed gives the same records, so the same hash.
/// </summary>
internal static class ReplayHashes
{
    /// <summary>The hash of what a run reads.</summary>
    /// <param name="parameters">The run's parameters.</param>
    /// <param name="settings">The settings snapshot.</param>
    /// <param name="scopeHash">The hash of the scope (see <see cref="Scope"/>).</param>
    /// <param name="versions">The strategy version hashes by strategy id.</param>
    /// <param name="barHash">The hash of the day bars the run reads.</param>
    public static string Input(ReplayParams parameters, ReplaySettingsSnapshot settings, string scopeHash, IReadOnlyDictionary<string, string> versions, string barHash)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(versions);
        return ReplayJson.Sha256(ReplayJson.Canonical(new { parameters, settings, scopeHash, versions = versions.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + v.Value), barHash }));
    }

    /// <summary>The hash of the scope as it stood at the start (ADR 0272): the chosen baskets and instruments and the members of the universe at the first session, each in a fixed order.</summary>
    public static string Scope(ReplayScope scope, IReadOnlyList<Guid> membersAtStart)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(membersAtStart);
        return ReplayJson.Sha256(ReplayJson.Canonical(new { baskets = scope.BasketIds.Order(), instruments = scope.InstrumentIds.Order(), members = membersAtStart.Distinct().Order() }));
    }

    /// <summary>The hash of one session record.</summary>
    public static string Session(ReplaySessionRecord record) => ReplayJson.Sha256(ReplayJson.Canonical(record));

    /// <summary>The hash of the result: the session hashes in the order of their sessions.</summary>
    public static string Result(IEnumerable<string> sessionHashesInOrder) => ReplayJson.Sha256(string.Join('\n', sessionHashesInOrder));
}

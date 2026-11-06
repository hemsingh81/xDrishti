namespace XDrishti.Domain.Learning;

/// <summary>
/// The three owner switches of the ML safety programme (CF-10 part 10d-1, spec D6 to D8, ADR 0220), all default OFF, and the rule for which change needs a fresh password. Turning on <c>ml.influence.enabled</c> or <c>ml.auto_promote</c> raises how far a model may matter, so it needs
/// the owner and a fresh password for that act; <c>ml.auto_rollback</c> only lowers reliance on ML (spec review fix 2), so it needs the owner's session and the CSRF token and no password, and turning any switch OFF is the safe direction and needs no password either. Pure.
/// </summary>
public static class MlSwitch
{
    /// <summary>May a model influence a decision at all (the last term of the predicate). Default OFF.</summary>
    public const string Influence = "ml.influence.enabled";

    /// <summary>May the daily run promote a challenger whose predicate terms hold. Default OFF.</summary>
    public const string AutoPromote = "ml.auto_promote";

    /// <summary>May the daily run execute a rollback proposal. Default OFF.</summary>
    public const string AutoRollback = "ml.auto_rollback";

    /// <summary>The three names, in the order the settings list them.</summary>
    public static IReadOnlyList<string> Names { get; } = [Influence, AutoPromote, AutoRollback];

    /// <summary>Whether the name is one of the three switches.</summary>
    public static bool IsKnown(string? name) => name is { } n && Names.Contains(n, StringComparer.Ordinal);

    /// <summary>Whether a change asks for a fresh password: it turns <see cref="Influence"/> or <see cref="AutoPromote"/> ON. A value that is not given, OFF, or <see cref="AutoRollback"/> in either direction does not.</summary>
    public static bool NeedsFreshPassword(bool? influence, bool? autoPromote) => influence == true || autoPromote == true;
}

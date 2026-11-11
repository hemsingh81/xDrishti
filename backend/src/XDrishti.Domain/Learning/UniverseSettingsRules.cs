using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The three owner settings of the universe dimension (BK-11, ADR 0261), as the read side and the Lab use them.</summary>
/// <param name="MinTradesChild">Fewest trades a tier cell needs before it can be read in place of its all-shares parent.</param>
/// <param name="LinkMinTrades">Fewest trades a tier needs before its edge is listed as an observation.</param>
/// <param name="RegisteredBaskets">The pre-registered basket names a strategy x basket comparison may use (case-insensitive, the system prefixes ignored).</param>
/// <param name="AutoPropose">BK-11b: when on, <c>xd learn cells --by-universe</c> proposes each new observation as a hypothesis, as the actor <c>auto</c>. Off by default; it never applies a restriction or runs an experiment.</param>
public sealed record UniverseSettings(int MinTradesChild, int LinkMinTrades, IReadOnlyList<string> RegisteredBaskets, bool AutoPropose = false)
{
    /// <summary>The settings in force when the owner has not changed any.</summary>
    public static UniverseSettings Defaults { get; } = new(UniverseSettingsRules.DefaultMinTradesChild, UniverseSettingsRules.DefaultLinkMinTrades, UniverseSettingsRules.DefaultRegisteredBaskets);

    /// <summary>The registered list as the text that is stored and shown.</summary>
    public string RegisteredText => string.Join(',', RegisteredBaskets);
}

/// <summary>The keys, defaults, validation and registry rule of the universe settings. Pure.</summary>
public static class UniverseSettingsRules
{
    /// <summary>Fewest trades of a tier cell to be read in place of its parent.</summary>
    public const string MinTradesChildKey = "learning.universe_min_trades_child";

    /// <summary>Fewest trades of a tier to be listed as an observation.</summary>
    public const string LinkMinTradesKey = "learning.universe_link_min_trades";

    /// <summary>The pre-registered basket names, comma separated.</summary>
    public const string RegisteredBasketsKey = "learning.universe_registered_baskets";

    /// <summary>The auto option of the proposer (BK-11b): <c>true</c> or <c>false</c>; no row is off.</summary>
    public const string AutoProposeKey = "learning.universe_link.auto_propose";

    /// <summary>Default of <see cref="MinTradesChildKey"/>.</summary>
    public const int DefaultMinTradesChild = 30;

    /// <summary>Default of <see cref="LinkMinTradesKey"/>.</summary>
    public const int DefaultLinkMinTrades = 100;

    /// <summary>The longest name and the most names of the registered list.</summary>
    public const int MaxNameLength = 60;

    /// <summary>The most names in the registered list.</summary>
    public const int MaxNames = 20;

    /// <summary>The research note's eight baskets (the eighth, Nifty 500, is a placeholder until the owner names it): Nifty 50, Nifty 100, Midcap 150, F&amp;O, top-100 liquidity, top-100 low volatility, top-100 momentum.</summary>
    public static IReadOnlyList<string> DefaultRegisteredBaskets { get; } = ["Nifty 50", "Nifty 100", "Nifty Midcap 150", "F&O stocks", "Most liquid", "Low volatility", "Momentum", "Nifty 500"];

    private static readonly string[] Prefixes = ["NSE ", "List: ", "Derived: "];

    /// <summary>The names of a comma separated list, trimmed and without blanks or repeats; null and an empty list when the text is not a usable list (see <see cref="ListProblem"/>).</summary>
    public static IReadOnlyList<string> ParseList(string? text) =>
        [.. (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Why a list is not usable, or null.</summary>
    public static string? ListProblem(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count is < 1 or > MaxNames)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Name 1 to {MaxNames} baskets.");
        }

        return names.Any(n => n.Length > MaxNameLength) ? string.Create(CultureInfo.InvariantCulture, $"A basket name is at most {MaxNameLength} characters.") : null;
    }

    // shortcut: baskets are registered by name, not id, because the default list names the research note's baskets before any of them need exist; a renamed basket changes its standing (ADR 0261)
    /// <summary>True when the basket is in the list: the comparison ignores case and the system prefixes <c>NSE </c>, <c>List: </c> and <c>Derived: </c> on either side.</summary>
    public static bool IsRegistered(string? basketName, IReadOnlyList<string> registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        if (string.IsNullOrWhiteSpace(basketName))
        {
            return false;
        }

        var wanted = Strip(basketName);
        return registered.Any(r => string.Equals(Strip(r), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The text a refusal prints so the owner sees what is registered.</summary>
    public static string RegisteredMessage(IReadOnlyList<string> registered) => "The registered baskets are: " + string.Join(", ", registered) + ".";

    /// <summary>The hash of one change, for the audit trail.</summary>
    public static string Hash(string key, string oldValue, string newValue, string actor) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', key, oldValue, newValue, actor))));

    private static string Strip(string name)
    {
        var t = name.Trim();
        foreach (var p in Prefixes)
        {
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                return t[p.Length..].Trim();
            }
        }

        return t;
    }
}

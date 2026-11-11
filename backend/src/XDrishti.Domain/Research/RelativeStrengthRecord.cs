using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>What a run that names <c>mom_rank</c>, <c>rs_vs_index</c> or <c>sector_rs</c> read (BK-09): the hash and count of the stored rows, the session the members are as of and, per pre-registered basket, its id, name, parameter hash (derived baskets) and membership basis. Part of the run key. Null for any other run, whose key is unchanged.</summary>
/// <param name="Sha256">Lower-case hex SHA-256 of the canonical rows read.</param>
/// <param name="Rows">Rows read.</param>
/// <param name="MembersAsOf">The last session of the window: the rank of day D uses the members as of the previous session.</param>
/// <param name="Baskets">The pre-registered baskets found, with their basis.</param>
/// <param name="Version">The version of the rules (<c>rsf-1</c>).</param>
/// <param name="FeatureUnknown">The steps at which a condition naming one of the features was not true because the feature was unknown (<c>feature_unknown</c>): counted, never a pass. Not part of the run key (it is a result of the run, not an input).</param>
public sealed record RelativeStrengthRecord(string Sha256, int Rows, DateOnly MembersAsOf, IReadOnlyList<RelativeStrengthBasis> Baskets, string Version, long FeatureUnknown = 0)
{
    public const string CurrentVersion = "rsf-1";

    /// <summary>The label line of the run: the baskets with their basis, "members as of" and parameter hash, and what the rows are.</summary>
    public string Label()
    {
        var asOf = MembersAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var baskets = Baskets.Count == 0
            ? "no pre-registered basket found"
            : string.Join("; ", Baskets.Select(b => b.Name + " (basket " + b.BasketId.ToString("D") + ", members as of " + asOf + ", " + b.Basis + (b.Basis == "single_snapshot" ? ", survivorship applies" : string.Empty) + ", params " + (b.ParamsHash ?? "-") + ")"));
        return "relative strength: " + baskets + "; feature_unknown steps: " + FeatureUnknown.ToString(CultureInfo.InvariantCulture) + "; stored rows of the previous closed session, not a forecast (" + Version + ")";
    }
}

/// <summary>One pre-registered basket a run read: its id, name, the parameter hash of its newest version (null for a manual or index basket) and the basis, <c>dated</c> or <c>single_snapshot</c>.</summary>
public sealed record RelativeStrengthBasis(Guid BasketId, string Name, string? ParamsHash, string Basis);

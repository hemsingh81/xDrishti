namespace XDrishti.Domain.Research;

/// <summary>The labels a run writes about its universe (spec P3-04, Stable names): the survivorship caveat is part of the record (doc 19 §1).</summary>
public static class UniverseLabels
{
    /// <summary>A basket universe: its members now, not as they were on the dates replayed (the system has no historical index membership, ISS-22).</summary>
    public const string MembersAsOfToday = "members as of today";

    /// <summary>The first words of the label of a basket run (BK-11): the members are the basket's members on the day of the run, one snapshot, never point in time.</summary>
    public const string BasketBasis = "members as of the run date (single_snapshot, survivorship applies)";

    /// <summary>The label of a basket run (BK-11, criterion 8): the basket id, the basis and the parameter hash (none for a manual or index basket; the hash of a derived basket is not read here). It depends only on the run, never on the owner's mutable registered list, so the run key is reproducible; whether a basket is registered is read when a report is built.</summary>
    public static string Basket(Guid basketId) => BasketBasis + "; basket " + basketId.ToString("D") + "; params -";

    /// <summary>An explicit list of instruments.</summary>
    public const string InstrumentsAsListed = "instruments as listed";
}

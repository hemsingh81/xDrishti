namespace XDrishti.Domain.Research;

/// <summary>The labels a run writes about its universe (spec P3-04, Stable names): the survivorship caveat is part of the record (doc 19 §1).</summary>
public static class UniverseLabels
{
    /// <summary>A basket universe: its members now, not as they were on the dates replayed (the system has no historical index membership, ISS-22).</summary>
    public const string MembersAsOfToday = "members as of today";

    /// <summary>An explicit list of instruments.</summary>
    public const string InstrumentsAsListed = "instruments as listed";
}

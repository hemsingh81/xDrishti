namespace XDrishti.Strategies.Model;

/// <summary>
/// The rules of one side. At every closed bar of the evaluation timeframe the sections are evaluated together: an entry is possible when <see cref="Setup"/> is true, <see cref="Trigger"/> is true and
/// <see cref="Invalidate"/> is not true. <see cref="Trigger"/>, <see cref="Entry"/>, <see cref="Stop"/> and <see cref="Exits"/> are required; <see cref="Setup"/>, <see cref="Invalidate"/> and
/// <see cref="Overlays"/> are optional; a list that is present has at least one item. A null section is "absent as written": the compile decides whether that is allowed. Equal by value.
/// </summary>
public sealed record SideRules(
    ConditionList? Setup,
    ConditionList? Trigger,
    ConditionList? Invalidate,
    EntrySpec? Entry,
    StopSpec? Stop,
    IReadOnlyList<ExitSpec>? Exits,
    IReadOnlyList<OverlaySpec>? Overlays)
{
    /// <inheritdoc />
    public bool Equals(SideRules? other) =>
        other is not null
        && Equals(Setup, other.Setup) && Equals(Trigger, other.Trigger) && Equals(Invalidate, other.Invalidate) && Equals(Entry, other.Entry) && Equals(Stop, other.Stop)
        && ListEquality.Equal(Exits, other.Exits) && ListEquality.Equal(Overlays, other.Overlays);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Setup, Trigger, Invalidate, Entry, Stop, ListEquality.Hash(Exits), ListEquality.Hash(Overlays));
}

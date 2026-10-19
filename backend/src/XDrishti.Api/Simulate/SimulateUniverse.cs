using XDrishti.Domain.Research;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The universe of a verb run as the verb resolved it (spec P3-04b2, D4, D7): what to hand the handler (the instrument ids in the order given, or the basket id), what the tick check needs (one probe per instrument, read without any bar), and the
/// notes the summary prints. <see cref="Problems"/> is non-empty when the universe is refused (exit 1); nothing is then run.
/// </summary>
internal sealed record SimulateUniverse(
    IReadOnlyList<Guid>? InstrumentIds,
    Guid? BasketId,
    IReadOnlyList<TickProbe> Probes,
    IReadOnlyList<string> Retired,
    int Duplicates,
    IReadOnlyDictionary<string, int> NotChecked,
    IReadOnlyList<string> Problems)
{
    /// <summary>The note key for an instrument type the verb does not know (not gated).</summary>
    public const string UnknownType = "unknown type";

    /// <summary>A refused universe.</summary>
    public static SimulateUniverse Refused(IReadOnlyList<string> problems) =>
        new(null, null, [], [], 0, new Dictionary<string, int>(), problems);

    public bool IsRefused => Problems.Count > 0;
}

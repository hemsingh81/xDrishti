using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The ordinary invented sheet <c>fixture-a</c> of P3-03f: the numbers of the TEST sheet (<see cref="TestCostSheet"/>) under an id that does not start with <c>test-</c>, so that the store and the verbs accept it. <b>Synthetic numbers, compiled into the
/// test projects only</b>: not a broker's rates, never stored in the owner's database, never read from <c>data/</c>. The owner's own sheet (plan question Q3) does not exist yet.
/// </summary>
internal static class FixtureSheet
{
    public const string Id = "fixture-a";

    /// <summary>The sheet: schedule A from 2026-01-01 and schedule B from 2026-03-03, as the TEST sheet.</summary>
    public static CostSheet Sheet { get; } = Build(Id, null);

    /// <summary>The canonical text of the sheet.</summary>
    public static string Json => Sheet.ToCanonicalJson();

    /// <summary>The same sheet under another id and with a <c>checked</c> note (both optional).</summary>
    public static CostSheet Build(string id, string? checkedNote) =>
        CostSheet.Create(
            id,
            "FIXTURE sheet (invented numbers)",
            "invented for tests; not a broker's sheet",
            checkedNote,
            [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA), TestCostSheet.Schedule(TestCostSheet.ScheduleB, TestCostSheet.IntradayB, TestCostSheet.DeliveryB)]).Value;
}

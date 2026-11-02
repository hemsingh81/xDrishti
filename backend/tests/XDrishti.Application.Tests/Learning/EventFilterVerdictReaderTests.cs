using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Selection;
using static XDrishti.Application.Tests.Learning.EvaluationKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>P15-10 criterion 6: the real reader over stored rows answers for the N of the effective switch, and answers null (unavailable) otherwise.</summary>
public sealed class EventFilterVerdictReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExtensionEvaluationRecord Record(int n, string verdict, string id, string rule = "evf-1", bool hold = false, string? development = null) =>
        new(id.PadRight(64, '0'), "filter:results", "F", null, $"{rule}:N={n}", "none", new string('a', 64), null, null, 3, 1.4, "quarter", 8, 8, 100, null, "{}", verdict, [], [], "{}", "{\"sources\":{\"development_record\":\"" + (development is null ? "none" : development.PadRight(64, '0')) + "\"}}", hold);

    private static async Task<EventFilterVerdict?> Ask(int switchDays, params ExtensionEvaluationRecord[] records)
    {
        var store = new Store([]);
        foreach (var r in records)
        {
            await store.AppendAsync(r, DateTimeOffset.UnixEpoch, Ct);
        }

        return await new EventFilterVerdictReader(store, new Switch(switchDays)).NewestAsync("evf-1", Ct);
    }

    [Fact]
    public async Task A_keep_for_the_switchs_n_is_returned_with_its_record_id_as_the_reference()
    {
        var verdict = await Ask(2, Record(2, "KEEP", "a2"));

        verdict.ShouldNotBeNull();
        verdict.State.ShouldBe("KEEP");
        verdict.RuleVersion.ShouldBe("evf-1");
        verdict.Reference.ShouldBe("a2".PadRight(64, '0'));
    }

    [Fact]
    public async Task A_keep_for_another_n_is_no_verdict_for_this_switch()
    {
        (await Ask(3, Record(2, "KEEP", "a2"))).ShouldBeNull();
    }

    [Fact]
    public async Task A_verdict_of_another_rule_version_or_no_verdict_at_all_is_null()
    {
        (await Ask(2, Record(2, "KEEP", "a2", rule: "evf-0"))).ShouldBeNull();
        (await Ask(2)).ShouldBeNull();
    }

    [Fact]
    public async Task The_newest_verdict_for_the_n_wins_so_a_later_drop_or_a_hold_out_that_did_not_confirm_replaces_a_keep()
    {
        (await Ask(2, Record(2, "KEEP", "a1"), Record(2, "DROP", "a2")))!.State.ShouldBe("DROP");
        (await Ask(2, Record(2, "KEEP", "a1"), Record(2, "INCONCLUSIVE", "a2", hold: true)))!.State.ShouldBe("INCONCLUSIVE");
        (await Ask(2, Record(2, "DROP", "a1"), Record(2, "KEEP", "a2"), Record(3, "DROP", "a3")))!.State.ShouldBe("KEEP");
    }

    [Fact]
    public async Task Not_evaluable_is_returned_as_a_verdict_and_is_never_read_as_keep()
    {
        (await Ask(2, Record(2, "NOT_EVALUABLE", "a1")))!.State.ShouldBe("NOT_EVALUABLE");
    }

    [Fact]
    public async Task A_development_keep_is_not_confirmed_and_a_hold_out_keep_is()
    {
        (await Ask(2, Record(2, "KEEP", "a1")))!.HoldOutConfirmed.ShouldBeFalse();
        (await Ask(2, Record(2, "KEEP", "a1"), Record(2, "KEEP", "a2", hold: true, development: "a1")))!.HoldOutConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_development_keep_is_confirmed_only_by_a_hold_out_keep_that_names_that_very_record()
    {
        // The hold-out of the first population failed; new data made a second development KEEP: the failed reading says nothing about it.
        var failed = await Ask(2, Record(2, "KEEP", "a1"), Record(2, "INCONCLUSIVE", "a2", hold: true, development: "a1"), Record(2, "KEEP", "a3"));
        var other = await Ask(2, Record(2, "KEEP", "a1"), Record(2, "KEEP", "a2", hold: true, development: "a1"), Record(2, "KEEP", "a3"));

        failed!.HoldOutConfirmed.ShouldBeFalse();
        other!.State.ShouldBe("KEEP");
        other.Reference.ShouldBe("a3".PadRight(64, '0'));
        other.HoldOutConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_hold_out_that_did_not_confirm_is_the_newest_and_is_not_a_keep()
    {
        var verdict = await Ask(2, Record(2, "KEEP", "a1"), Record(2, "INCONCLUSIVE", "a2", hold: true, development: "a1"));

        verdict!.State.ShouldBe("INCONCLUSIVE");
        verdict.HoldOutConfirmed.ShouldBeFalse();
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.LearnedSettings;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>AU-10 criteria 2, 3, 6, 7 and 10: the weekly run.</summary>
public sealed class RunLearnedSettingsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeLearnedStore _store = new();
    private readonly FakeLearnedConfig _config = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    private RunLearnedSettingsHandler Handler(bool autoOn, LearnedEvidenceBatch batch, int cap = 3) =>
        new(new FakeEvidence(batch), _store, _config, new FakeAuto(autoOn), new LearnedSettingsOptions { WeeklyCap = cap, MaxStepPercent = 10 }, _clock, NullLogger<RunLearnedSettingsHandler>.Instance);

    private static LearnedEvidenceBatch Batch(params LearnedEvidenceItem[] items) => new(true, items);

    private static LearnedEvidenceItem Item(string key, string proposed, string hash = "e1") => new(key, proposed, "paper fills weeks 40-45", hash, true, false);

    private static ValueTask<XDrishti.Domain.Common.Result<LearnedRunView>> RunAsync(RunLearnedSettingsHandler h) => h.HandleAsync(new RunLearnedSettingsCommand("test"), Ct);

    [Fact]
    public async Task Auto_on_applies_a_tightening_recorded_as_auto_with_the_snapshot_and_the_evidence()
    {
        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2"))));

        result.Value.Applied.ShouldBe(1);
        var row = _store.Rows.Single();
        row.State.ShouldBe("applied");
        row.DecidedBy.ShouldBe("auto"); // never an owner approval
        row.DecidedBy!.StartsWith("owner:", StringComparison.Ordinal).ShouldBeFalse();
        row.Direction.ShouldBe("tighten");
        row.Snapshot.ShouldContain("\"auto\":true");
        row.Snapshot.ShouldContain(FakeLearnedConfig.BpsKey);
        row.SnapshotHash.Length.ShouldBe(64);
        row.EvidenceRef.ShouldBe("paper fills weeks 40-45");
        row.OldValue.ShouldBe("4");
        row.NewValue.ShouldBe("4.2");
    }

    [Fact]
    public async Task Auto_off_makes_a_proposal_only()
    {
        var result = await RunAsync(Handler(false, Batch(Item(FakeLearnedConfig.BpsKey, "4.2"))));

        result.Value.Proposed.ShouldBe(1);
        result.Value.Applied.ShouldBe(0);
        _store.Rows.Single().State.ShouldBe("proposed");
        _store.Decisions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_loosening_is_never_applied_by_the_run_at_any_auto_state(bool autoOn)
    {
        await RunAsync(Handler(autoOn, Batch(Item(FakeLearnedConfig.BpsKey, "3.5"))));

        var row = _store.Rows.Single();
        row.State.ShouldBe("proposed");
        row.Direction.ShouldBe("loosen");
        _store.Decisions.ShouldBeEmpty(); // no decision of any kind was even attempted
    }

    [Fact]
    public async Task Unavailable_evidence_changes_nothing()
    {
        var result = await RunAsync(Handler(true, LearnedEvidenceBatch.Unavailable));

        _store.Rows.ShouldBeEmpty();
        result.Value.Proposed.ShouldBe(0);
        result.Value.Outcome.ShouldContain("nothing changed");
    }

    [Fact]
    public async Task Evidence_that_is_sealed_or_not_out_of_sample_changes_nothing()
    {
        await RunAsync(Handler(true, Batch(new LearnedEvidenceItem(FakeLearnedConfig.BpsKey, "4.2", "r", "h", true, true), new LearnedEvidenceItem("Confidence:Decay:Sigmas", "0.9", "r", "h2", false, false))));

        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_off_the_allow_list_or_without_a_direction_is_skipped()
    {
        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.LoopLevel, "0.97"), Item(FakeLearnedConfig.NoDirectionKey, "60"))));

        _store.Rows.ShouldBeEmpty();
        result.Value.Skipped.Select(s => s.Code).ShouldAllBe(c => c == "learned.not_learnable");
    }

    [Fact]
    public async Task The_weekly_cap_stops_automatic_application_and_the_rest_stay_proposals()
    {
        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2"), Item("Confidence:Decay:Sigmas", "0.95")), cap: 1));

        result.Value.Applied.ShouldBe(1);
        result.Value.Proposed.ShouldBe(2);
        _store.Rows.Count(r => r.State == "applied").ShouldBe(1);
        _store.Rows.Count(r => r.State == "proposed").ShouldBe(1);
    }

    [Fact]
    public async Task A_cap_of_zero_applies_nothing()
    {
        await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2")), cap: 0));

        _store.Rows.Single().State.ShouldBe("proposed");
    }

    [Fact]
    public async Task A_candidate_that_fails_the_whole_configuration_check_is_refused_and_nothing_is_saved()
    {
        _config.Check = c => c.ContainsKey(FakeLearnedConfig.BpsKey) ? ["pair rule broken"] : [];

        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2"))));

        _store.Rows.ShouldBeEmpty();
        result.Value.Skipped.Single().Code.ShouldBe("learned.config_invalid");
    }

    [Fact]
    public async Task The_same_evidence_twice_is_one_change()
    {
        var h = Handler(false, Batch(Item(FakeLearnedConfig.BpsKey, "4.2")));

        await RunAsync(h);
        var second = await RunAsync(h);

        _store.Rows.Count.ShouldBe(1);
        second.Value.Skipped.Single().Code.ShouldBe("learned.already_seen");
    }

    [Fact]
    public async Task When_the_database_refuses_the_automatic_decision_the_change_stays_a_proposal()
    {
        _store.AutoSwitchOn = false; // the stored switch is not true: the trigger refuses

        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2"))));

        result.Value.Applied.ShouldBe(0);
        _store.Rows.Single().State.ShouldBe("proposed");
    }

    [Fact]
    public async Task One_variable_per_change_the_second_piece_of_evidence_for_a_key_is_left_alone()
    {
        var result = await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "4.2", "e1"), Item(FakeLearnedConfig.BpsKey, "4.3", "e2"))));

        _store.Rows.Count.ShouldBe(1);
        result.Value.Skipped.Single().Code.ShouldBe("learned.one_variable");
    }

    [Fact]
    public async Task The_next_change_steps_from_the_value_already_applied()
    {
        await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "5", "e1"))));
        await RunAsync(Handler(true, Batch(Item(FakeLearnedConfig.BpsKey, "5", "e2"))));

        _store.Rows.Select(r => (r.OldValue, r.NewValue)).ShouldBe([("4", "4.4"), ("4.4", "4.84")]);
    }
}

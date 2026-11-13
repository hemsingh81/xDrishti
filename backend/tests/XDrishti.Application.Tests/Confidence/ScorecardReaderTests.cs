#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 part 1 (ADR 0221): the scorecard assembled from scripted producers. A producer that does not exist is an input that does not exist, and an input that does not exist is a fail, never a pass.</summary>
public sealed class ScorecardReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ScorecardView> CardAsync(ReadinessFixture f) => await f.Reader.BuildAsync(ReadinessFixture.Strategy, 1, Ct);

    private static string Status(ScorecardView card, int row) => card.Rows.Single(r => r.Number == row).Status;

    [Fact]
    public async Task With_every_producer_answering_and_nothing_wrong_every_row_passes_and_the_score_is_ready()
    {
        var f = new ReadinessFixture();

        var card = await CardAsync(f);

        card.Rows.Count.ShouldBe(15);
        card.Rows.Where(r => r.Status != "pass").Select(r => $"{r.Number}: {r.Value}").ShouldBeEmpty();
        card.RowsPassed.ShouldBe(15);
        card.GateR.Passed.ShouldBeTrue();
        card.GateS.Passed.ShouldBeTrue();
        card.Score.ShouldBe(100);
        card.Label.ShouldBe("ready");
        card.HardFails.ShouldBeEmpty();
        card.SubScores.Single(s => s.Name == SubScore.CalibrationName).State.ShouldBe("not_applicable", "no champion model, so no calibration");
        card.SubScores.Where(s => s.State == "scored").ShouldAllBe(s => s.Score > 0);
        card.Strategy.ShouldBe("ORB");
        card.Version.ShouldBe(1);
    }

    [Fact]
    public async Task The_real_gaps_say_unavailable_so_the_rows_without_a_producer_fail_and_the_card_is_not_ready()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps(), PackGateRow = null };

        // the block an older run wrote: tiers A and B, no measured breach, every capacity reading unavailable (the fill minute was not read)
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = BlockBeforeS2b() } };

        var card = await CardAsync(f);

        foreach (var row in new[] { 14, 15 })
        {
            Status(card, row).ShouldBe("unavailable", $"row {row}");
        }

        Status(card, 13).ShouldBe("pass", "row 13 reads the Lab verdict's benchmark comparison since SC-S4b, not the gaps");
        Status(card, 10).ShouldBe("pass", "rows 10 and 11 read the Lab verdict since SC-S1, not the gaps");
        Status(card, 11).ShouldBe("pass");
        Status(card, 2).ShouldBe("pass");
        Status(card, 3).ShouldBe("pass");
        card.GateR.Passed.ShouldBeFalse();
        card.GateS.Passed.ShouldBeFalse();
        card.HardFails.ShouldBeEmpty("the integrity and tier C hard fails have their sources since SC-S3 (ADR 0224) and read clear here");
        card.Waiting.ShouldNotContain(w => w.Contains("row 13", StringComparison.Ordinal));
        card.RawMinimum.ShouldBeGreaterThan(0, "the score without the faults is still shown");
    }

    /// <summary>A block as a run writes it since S2b: every fill minute read unless <paramref name="minuteUnavailable"/> says otherwise.</summary>
    private static CostStressTiersView Tiers(int a = 300, int b = 30, int c = 0, int none = 0, int breaches = 0, int capacityNone = 0, int minuteUnavailable = 0) =>
        new(a + b + c + none, a, b, c, none, breaches, capacityNone, string.Empty, string.Empty, string.Empty, 0, a + b + c + none - minuteUnavailable, minuteUnavailable, 5000);

    /// <summary>A block as the slice before S2b wrote it: no minute members, every capacity reading unavailable.</summary>
    private static CostStressTiersView BlockBeforeS2b(int breaches = 0, int capacityNone = 330) =>
        new(330, 300, 30, 0, 0, breaches, capacityNone, string.Empty, string.Empty, string.Empty, 0);

    // ---- SC-S3 (ADR 0224): the integrity hard fail ----

    private static string Note(ScorecardView card, string term) => card.Notes.Single(n => n.StartsWith($"Hard fail \"{term}\":", StringComparison.Ordinal));

    [Fact]
    public async Task Integrity_criterion_MET_is_clear_and_the_card_says_why()
    {
        var card = await CardAsync(new ReadinessFixture());

        card.HardFails.ShouldNotContain(h => h.Contains("integrity", StringComparison.Ordinal));
        card.NotApplicable.ShouldNotContain(h => h.Contains("integrity", StringComparison.Ordinal));
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("clear");
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("criterion I");
    }

    [Fact]
    public async Task Integrity_criterion_NOT_MET_is_a_hard_fail_that_zeroes_the_score_and_shows_the_disagreement()
    {
        var f = new ReadinessFixture
        {
            IntegrityView = new CaptureIntegrityView("NOT MET", "NOT MET: 2 stored entries disagreed with a later replay (sessions 2026-11-02)", 40, new DateOnly(2026, 11, 6), []),
        };

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(ReadinessCalculator.IntegrityName);
        card.Score.ShouldBe(0);
        card.Label.ShouldBe("not_ready");
        card.RawMinimum.ShouldBeGreaterThan(0);
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("hard fail");
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("2 stored entries disagreed");
    }

    [Fact]
    public async Task Integrity_with_no_capture_run_yet_is_not_applicable_neither_a_fault_nor_a_pass()
    {
        var f = new ReadinessFixture { IntegrityView = new CaptureIntegrityView("NOT EVALUABLE", "NOT EVALUABLE: no capture run of this epoch", 0, new DateOnly(2026, 11, 6), []) };

        var card = await CardAsync(f);

        card.HardFails.ShouldNotContain(h => h.Contains("integrity", StringComparison.Ordinal));
        card.NotApplicable.ShouldContain(ReadinessCalculator.IntegrityName);
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("not applicable");
        Note(card, ReadinessCalculator.IntegrityName).ShouldNotContain("clear");
    }

    [Fact]
    public async Task Integrity_that_cannot_be_read_is_unavailable_a_hard_fail_and_not_a_detected_fault()
    {
        var f = new ReadinessFixture { IntegrityFailure = new InvalidOperationException("db down") };

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.IntegrityName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        card.HardFails.ShouldNotContain(ReadinessCalculator.IntegrityName, "unavailable is not the detected fault");
        card.Score.ShouldBe(0);
        Note(card, ReadinessCalculator.IntegrityName).ShouldContain("could not be read");
    }

    [Fact]
    public async Task An_unknown_verdict_word_is_unavailable_never_clear()
    {
        var f = new ReadinessFixture { IntegrityView = new CaptureIntegrityView("MAYBE", "?", 3, null, []) };

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(h => h.Contains("integrity fault (the input is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_integrity_criterion_is_read_once_for_all_strategies_of_a_scope()
    {
        var f = new ReadinessFixture();
        var reader = f.Reader;

        await reader.BuildAsync(ReadinessFixture.Strategy, 1, Ct);
        await reader.BuildAsync(ReadinessFixture.Strategy, 1, Ct);

        await f.Integrity.Received(1).HandleAsync(Arg.Any<GetCaptureIntegrityQuery>(), Arg.Any<CancellationToken>());
    }

    // ---- SC-S3 (ADR 0224): the tier C hard fail, judged on the Lab and the forward trades ----

    private static ReadinessFixture AllLabTierC()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 0, b: 0, c: 330) } };
        return f;
    }

    private static ReadinessFixture AllForwardTierC() => new() { DefaultTradedValue = 5m * LiquidityTiers.Crore };

    private static bool TierFault(ScorecardView card) => card.HardFails.Contains(ReadinessCalculator.TierName);

    [Fact]
    public async Task Mixed_A_and_C_in_both_sets_is_not_a_hard_fail()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 100, b: 0, c: 230) } };
        f.ValueBySymbol["S0"] = 5m * LiquidityTiers.Crore; // one forward trade tier C, the rest tier A

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        card.HardFails.ShouldNotContain(h => h.Contains("tier C", StringComparison.Ordinal));
        Note(card, ReadinessCalculator.TierName).ShouldStartWith($"Hard fail \"{ReadinessCalculator.TierName}\": clear");
        Note(card, ReadinessCalculator.TierName).ShouldContain("A 100, B 0, C 230, unavailable 0");
        Note(card, ReadinessCalculator.TierName).ShouldContain("A 329, B 0, C 1, unavailable 0");
    }

    [Fact]
    public async Task A_Lab_set_of_only_tier_C_is_a_hard_fail_triggered_by_the_Lab_even_when_the_forward_trades_are_tier_A()
    {
        var card = await CardAsync(AllLabTierC());

        TierFault(card).ShouldBeTrue();
        card.Score.ShouldBe(0);
        var note = Note(card, ReadinessCalculator.TierName);
        note.ShouldContain("triggered by: Lab out-of-sample trades");
        note.ShouldNotContain("triggered by: Lab out-of-sample trades, forward");
        note.ShouldContain("Lab out-of-sample trades: A 0, B 0, C 330, unavailable 0 (only tier C)");
        note.ShouldContain("forward shadow and paper trades: A 330, B 0, C 0, unavailable 0 (not only tier C)");
    }

    [Fact]
    public async Task A_forward_set_of_only_tier_C_is_a_hard_fail_triggered_by_the_forward_trades_even_when_the_Lab_is_tier_A()
    {
        var card = await CardAsync(AllForwardTierC());

        TierFault(card).ShouldBeTrue();
        var note = Note(card, ReadinessCalculator.TierName);
        note.ShouldContain("triggered by: forward shadow and paper trades");
        note.ShouldContain("forward shadow and paper trades: A 0, B 0, C 330, unavailable 0 (only tier C)");
        note.ShouldContain("Lab out-of-sample trades: A 300, B 30, C 0, unavailable 0 (not only tier C)");
    }

    [Fact]
    public async Task Both_sets_only_tier_C_name_both_as_the_trigger()
    {
        var f = AllForwardTierC();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 0, b: 0, c: 330) } };

        var card = await CardAsync(f);

        Note(card, ReadinessCalculator.TierName).ShouldContain("triggered by: Lab out-of-sample trades, forward shadow and paper trades");
    }

    [Fact]
    public async Task Tier_B_is_not_tier_C_so_a_forward_set_of_only_tier_B_is_clear()
    {
        var f = new ReadinessFixture { DefaultTradedValue = 50m * LiquidityTiers.Crore };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: A 0, B 330, C 0, unavailable 0");
    }

    [Fact]
    public async Task No_forward_trades_is_not_tier_C_and_the_Lab_set_alone_decides()
    {
        var f = new ReadinessFixture { ShadowRows = [] };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: no trades (not tier C)");
        await f.BarQueries.DidNotReceiveWithAnyArgs().ReadDailyBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_shadow_rows_read_that_throws_makes_the_forward_tier_set_unavailable_and_the_card_still_builds()
    {
        var f = new ReadinessFixture { TierShadowFailure = new InvalidOperationException("db down") };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.TierName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: unavailable");
        card.Label.ShouldNotBe("ready");
    }

    [Fact]
    public async Task No_forward_trades_and_a_Lab_set_of_only_tier_C_is_a_hard_fail()
    {
        var f = AllLabTierC();
        f.ShadowRows = [];

        var card = await CardAsync(f);

        TierFault(card).ShouldBeTrue();
    }

    [Fact]
    public async Task A_Lab_block_with_no_trades_is_absent_not_tier_C()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 0, b: 0, c: 0) } };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        Note(card, ReadinessCalculator.TierName).ShouldContain("Lab out-of-sample trades: no trades (not tier C)");
    }

    [Fact]
    public async Task No_trades_in_either_set_is_not_tier_C()
    {
        var f = new ReadinessFixture { ShadowRows = [] };
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 0, b: 0, c: 0) } };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        card.HardFails.ShouldNotContain(h => h.Contains("tier C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Lab_verdict_without_the_tier_block_is_unavailable_not_tier_C_and_a_forward_set_of_only_tier_C_still_fails_it()
    {
        var older = new ReadinessFixture();
        older.Verdict = older.Verdict! with { CostStress = older.Verdict.CostStress! with { Tiers = null } };

        var card = await CardAsync(older);

        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.TierName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        card.HardFails.ShouldNotContain(ReadinessCalculator.TierName);
        Note(card, ReadinessCalculator.TierName).ShouldContain("Lab out-of-sample trades: unavailable");

        var both = AllForwardTierC();
        both.Verdict = both.Verdict! with { CostStress = both.Verdict.CostStress! with { Tiers = null } };
        TierFault(await CardAsync(both)).ShouldBeTrue("a detected fault in one set stands whatever the other set says");
    }

    [Fact]
    public async Task Lab_trades_that_are_tier_C_or_without_a_tier_are_unavailable_not_tier_C()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 0, b: 0, c: 300, none: 30) } };

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.TierName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lab_trades_with_an_A_and_some_unavailable_are_not_only_tier_C()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = Tiers(a: 1, b: 0, c: 300, none: 29) } };

        var card = await CardAsync(f);

        card.HardFails.ShouldNotContain(h => h.Contains("tier C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Forward_trades_whose_symbol_does_not_resolve_are_unavailable_never_tier_C()
    {
        var f = new ReadinessFixture();
        foreach (var i in Enumerable.Range(0, 330))
        {
            f.Unresolved.Add($"S{i}");
        }

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.TierName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: unavailable");
        Note(card, ReadinessCalculator.TierName).ShouldContain("A 0, B 0, C 0, unavailable 330");
    }

    [Fact]
    public async Task An_ambiguous_symbol_gives_an_unavailable_tier()
    {
        var f = new ReadinessFixture { ShadowRows = ReadinessFixture.Series(1), Ambiguous = "S0" };

        var card = await CardAsync(f);

        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: unavailable");
    }

    [Fact]
    public async Task Fewer_than_20_sessions_of_bars_is_an_unavailable_tier_not_tier_C()
    {
        var f = new ReadinessFixture { ShadowRows = ReadinessFixture.Series(2) };
        f.BarQueries.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>([]));

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        Note(card, ReadinessCalculator.TierName).ShouldContain("forward shadow and paper trades: unavailable");
    }

    [Fact]
    public async Task Forward_bars_that_cannot_be_read_make_that_instruments_trades_unavailable_and_the_card_still_builds()
    {
        var f = new ReadinessFixture { ShadowRows = ReadinessFixture.Series(3) };
        f.UnreadableBars.Add(ReadinessFixture.IdOf("S1"));

        var card = await CardAsync(f);

        TierFault(card).ShouldBeFalse();
        Note(card, ReadinessCalculator.TierName).ShouldContain("A 2, B 0, C 0, unavailable 1");
    }

    [Fact]
    public async Task An_unreadable_forward_source_is_an_unavailable_set_never_a_pass()
    {
        var f = new ReadinessFixture();
        f.Instruments.FindForResolveAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<ResolveCandidate>>>(_ => throw new InvalidOperationException("db down"));

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.TierName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        card.Score.ShouldBe(0);
    }

    [Fact]
    public async Task The_tier_is_taken_from_the_sessions_before_the_entry_day_and_each_instrument_is_read_once_and_bounded()
    {
        var f = new ReadinessFixture { ShadowRows = [.. ReadinessFixture.Series(3).Select((r, i) => r with { TradeKey = $"{ReadinessFixture.Strategy}|ABC|BUY|{i}" })] };

        await CardAsync(f);

        await f.BarQueries.Received(1).ReadDailyBarsAsync(Arg.Any<Guid>(), new DateOnly(2025, 1, 1).AddDays(-60), new DateOnly(2025, 1, 3).AddDays(1), Arg.Any<CancellationToken>());
    }


    private static ReadinessFixture WithTiers(CostStressTiersView? tiers)
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { CostStress = f.Verdict.CostStress! with { Tiers = tiers } };
        return f;
    }

    [Fact]
    public async Task Row_14_passes_when_every_trade_is_tier_A_or_B_every_fill_minute_was_read_and_every_order_is_within_both_caps()
    {
        var card = await CardAsync(WithTiers(Tiers()));

        Status(card, 14).ShouldBe("pass");
        card.Rows.Single(r => r.Number == 14).Value.ShouldContain("within capacity");
    }

    [Theory]
    [InlineData(1, 0, "tier C")]
    [InlineData(0, 1, "participation cap")]
    public async Task Row_14_fails_on_any_tier_C_trade_or_any_capacity_breach(int tierC, int breaches, string said)
    {
        var card = await CardAsync(WithTiers(Tiers(c: tierC, breaches: breaches)));

        Status(card, 14).ShouldBe("fail");
        card.Rows.Single(r => r.Number == 14).Value.ShouldContain(said);
        card.GateR.Failing.Select(r => r.Number).ShouldContain(14);
    }

    [Fact]
    public async Task Row_14_is_a_fail_when_a_definite_failure_comes_with_unavailable_readings()
    {
        var card = await CardAsync(WithTiers(Tiers(c: 2, none: 3)));

        Status(card, 14).ShouldBe("fail");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task Row_14_is_unavailable_never_a_pass_when_a_tier_or_a_capacity_reading_could_not_be_made(int none, int capacityNone)
    {
        var card = await CardAsync(WithTiers(Tiers(none: none, capacityNone: capacityNone)));

        Status(card, 14).ShouldBe("unavailable");
        card.GateR.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Row_14_of_a_block_written_before_S2b_is_unavailable_because_the_fill_minute_was_not_read_and_a_breach_still_beats_it()
    {
        // every capacity reading unavailable (no fill minute), tiers A and B only, no measured breach: the block of a run before the fill-minute read
        var before = await CardAsync(WithTiers(BlockBeforeS2b()));
        var breach = await CardAsync(WithTiers(BlockBeforeS2b(breaches: 1, capacityNone: 329)));

        Status(before, 14).ShouldBe("unavailable");
        before.Rows.Single(r => r.Number == 14).Value.ShouldContain("before the fill minute's traded value was read");
        before.GateR.Passed.ShouldBeFalse();
        Status(breach, 14).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_14_of_a_block_without_the_minute_members_never_passes_even_when_its_other_counts_look_complete()
    {
        // a block that has no unavailable count and no breach but never recorded a fill-minute read: the 1 % cap was not judged
        var forged = new CostStressTiersView(330, 300, 30, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, 0);

        var card = await CardAsync(WithTiers(forged));

        Status(card, 14).ShouldBe("unavailable");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(330)]
    public async Task Row_14_is_unavailable_when_any_fill_minute_was_not_read_even_if_the_other_counts_are_clean(int minuteUnavailable)
    {
        var card = await CardAsync(WithTiers(Tiers(minuteUnavailable: minuteUnavailable)));

        Status(card, 14).ShouldBe("unavailable");
        card.Rows.Single(r => r.Number == 14).Value.ShouldContain($"{minuteUnavailable} have no readable fill minute");
        card.GateR.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Row_14_names_the_trades_over_the_fill_minute_bound()
    {
        var over = Tiers(minuteUnavailable: 30) with { MinuteReadLimit = 300 };

        var card = await CardAsync(WithTiers(over));

        card.Rows.Single(r => r.Number == 14).Value.ShouldContain("30 over the bound");
    }

    [Fact]
    public async Task Row_14_of_a_verdict_stored_before_the_block_says_re_run_and_is_not_re_run()
    {
        var f = WithTiers(null);

        var card = await CardAsync(f);

        Status(card, 14).ShouldBe("unavailable");
        card.Rows.Single(r => r.Number == 14).Value.ShouldContain("produced before the tier and capacity check; re-run");
    }

    [Fact]
    public async Task Row_14_of_a_verdict_with_no_trade_is_unavailable()
    {
        var card = await CardAsync(WithTiers(new CostStressTiersView(0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty)));

        Status(card, 14).ShouldBe("unavailable");
    }

    [Fact]
    public async Task With_no_lab_run_the_lab_rows_are_unavailable_and_the_card_says_what_to_run()
    {
        var f = new ReadinessFixture { Verdict = null };

        var card = await CardAsync(f);

        foreach (var row in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 14 })
        {
            Status(card, row).ShouldNotBe("pass", $"row {row}");
        }

        card.Rows.Single(r => r.Number == 2).Value.ShouldContain("No finished full Lab run");
        card.GateR.Passed.ShouldBeFalse();
        card.Notes.ShouldContain(n => n.Contains("No finished full Lab run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_strategy_with_no_version_reads_no_lab_result_at_all()
    {
        var f = new ReadinessFixture();

        var card = await f.Reader.BuildAsync(ReadinessFixture.Strategy, null, Ct);

        card.Version.ShouldBeNull();
        card.Rows.Single(r => r.Number == 1).Status.ShouldBe("unavailable");
        await f.Runs.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_registration_is_a_fail_and_an_unreadable_one_is_unavailable()
    {
        var missing = new ReadinessFixture { RegistrationResult = Result.Failure<RegistrationView>(Error.NotFound("lab.not_found", "none")) };
        var unreadable = new ReadinessFixture { RegistrationResult = Result.Failure<RegistrationView>(Error.Upstream("x", "down")) };

        Status(await CardAsync(missing), 1).ShouldBe("fail");
        Status(await CardAsync(unreadable), 1).ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_hold_out_not_yet_unlocked_is_unavailable_and_a_reused_one_is_a_hard_fail()
    {
        var none = new ReadinessFixture { HoldOutResults = [] };
        var reused = new ReadinessFixture();
        reused.HoldOutResults = [reused.HoldOutResults[0] with { ReusedWindow = true }];

        var noneCard = await CardAsync(none);
        var reusedCard = await CardAsync(reused);

        Status(noneCard, 12).ShouldBe("unavailable");
        noneCard.Rows.Single(r => r.Number == 12).Value.ShouldContain("not been unlocked");
        Status(reusedCard, 12).ShouldBe("fail");
        reusedCard.HardFails.ShouldContain(ReadinessCalculator.HoldOutName);
        reusedCard.Score.ShouldBe(0);
    }

    [Fact]
    public async Task Row_twelve_reads_the_newest_fresh_look_so_a_later_fail_beats_an_earlier_pass_and_a_newer_re_used_look_hides_nothing()
    {
        var f = new ReadinessFixture();
        var pass = f.HoldOutResults[0] with { UnlockedAt = DateTimeOffset.UnixEpoch.AddDays(1) };
        var laterFail = pass with { UnlockedAt = DateTimeOffset.UnixEpoch.AddDays(200), Passed = false, Status = "fail", Reason = "block 2 failed" };
        var newerReused = pass with { UnlockedAt = DateTimeOffset.UnixEpoch.AddDays(300), Passed = false, ReusedWindow = true, ReusedReason = "not_predated" };

        f.HoldOutResults = [pass, laterFail];
        var failCard = await CardAsync(f);
        f.HoldOutResults = [pass, newerReused];
        var passCard = await CardAsync(f);

        Status(failCard, 12).ShouldBe("fail");
        failCard.Rows.Single(r => r.Number == 12).Value.ShouldContain("block 2 failed");
        Status(passCard, 12).ShouldBe("pass");
    }

    [Fact]
    public async Task A_producer_that_judged_fail_is_taken_at_its_word()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Nulls = f.Verdict.Nulls! with { Mirror = f.Verdict.Nulls.Mirror! with { Status = "fail" } } };

        var card = await CardAsync(f);

        Status(card, 8).ShouldBe("fail");
        card.GateR.Passed.ShouldBeFalse();
        card.GateR.Failing.Select(r => r.Number).ShouldBe([8]);
    }

    // ---- rows 10 and 11 from the Lab verdict (SC-S1, ADR 0222) ----

    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static QuarterView Q(int year, int quarter, double mean) => new(year, quarter, 50, mean);

    private static ReadinessFixture WithRobustness(Func<RobustnessView, RobustnessView> change)
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Robustness = change(f.Verdict!.Robustness!) };
        return f;
    }

    [Fact]
    public async Task Row_10_passes_with_quarters_years_and_instruments_inside_their_limits()
    {
        var card = await CardAsync(new ReadinessFixture());

        var row = card.Rows.Single(r => r.Number == 10);

        row.Status.ShouldBe("pass");
        row.Value.ShouldContain("quarters 80 %");
        row.Value.ShouldContain("largest instrument 10 %");
    }

    [Fact]
    public async Task Row_10_fails_with_two_of_three_quarters_at_or_above_the_floor()
    {
        var f = WithRobustness(r => r with { ByQuarter = [Q(2025, 1, 0.1), Q(2025, 2, 0.2), Q(2025, 3, -0.4)] });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("fail");
        card.Rows.Single(r => r.Number == 10).Value.ShouldContain("quarters 67 %");
    }

    [Fact]
    public async Task Row_10_passes_a_quarter_with_a_mean_of_exactly_minus_0_05()
    {
        var f = WithRobustness(r => r with { ByQuarter = [Q(2025, 1, 0.1), Q(2025, 2, -0.05)] });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("pass");
    }

    [Fact]
    public async Task Row_10_fails_with_one_instrument_over_its_limit()
    {
        var f = WithRobustness(r => r with { MaxInstrumentShare = 0.4 });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_10_is_unavailable_for_a_verdict_stored_before_the_figures_and_says_to_re_run()
    {
        var f = WithRobustness(r => r with { ByQuarter = null, MaxInstrumentShare = null });

        var card = await CardAsync(f);

        var row = card.Rows.Single(r => r.Number == 10);
        row.Status.ShouldBe("unavailable");
        row.Value.ShouldContain("produced before the scorecard sources existed; re-run");
        card.Waiting.ShouldContain(w => w.Contains("row 10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Row_10_is_unavailable_when_the_verdict_has_no_robustness_block()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Robustness = null };

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("unavailable");
        card.Rows.Single(r => r.Number == 10).Value.ShouldNotContain("re-run");
    }

    [Fact]
    public async Task Row_11_passes_when_the_observed_fall_is_within_the_90th_percentile_and_the_95th_within_budget()
    {
        var card = await CardAsync(new ReadinessFixture());

        var row = card.Rows.Single(r => r.Number == 11);

        row.Status.ShouldBe("pass");
        row.Value.ShouldContain("90th 9 R");
    }

    [Fact]
    public async Task Row_11_fails_when_the_observed_fall_is_above_the_90th_percentile()
    {
        var f = WithRobustness(r => r with { MonteCarlo = r.MonteCarlo! with { DrawdownP90 = 4 } });

        var card = await CardAsync(f);

        Status(card, 11).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_11_is_unavailable_for_a_verdict_stored_before_the_90th_percentile_and_says_to_re_run()
    {
        var f = WithRobustness(r => r with { MonteCarlo = r.MonteCarlo! with { DrawdownP90 = null } });

        var card = await CardAsync(f);

        var row = card.Rows.Single(r => r.Number == 11);
        row.Status.ShouldBe("unavailable");
        row.Value.ShouldContain("produced before the scorecard sources existed; re-run");
    }

    [Fact]
    public async Task An_old_stored_verdict_without_the_new_members_reads_as_unavailable()
    {
        const string Old = """{"sensitivity":{"timeframe":"15m","exit":"FIXED_RR","base":null,"parameters":[],"label":""},"monteCarlo":{"trades":330,"draws":1000,"seed":"s","observedMeanR":0.2,"observedMaxDrawdownR":5,"meanP05":0.1,"meanP50":0.2,"meanP95":0.3,"drawdownP50":4,"drawdownP95":12,"shareNotPositive":0.1},"byYear":[],"signFlipBaseline":null,"byRegime":"","limits":[]}""";
        var old = System.Text.Json.JsonSerializer.Deserialize<RobustnessView>(Old, WebJson)!;
        old.MonteCarlo!.DrawdownP90.ShouldBeNull();
        old.ByQuarter.ShouldBeNull();
        old.MaxInstrumentShare.ShouldBeNull();

        var f = WithRobustness(_ => old);
        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("unavailable");
        Status(card, 11).ShouldBe("unavailable");
    }

    [Fact]
    public async Task Row_3_uses_the_larger_of_the_gate_factor_and_the_honest_factor()
    {
        // the gates used 1.25 (2 trials); the honest count of looks gives 2.5: 0.15 x 2.5 = 0.375 is over the 0.20 expectancy
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Trials = new TrialsView(1, 0, 0, 0, 0, 64, 2.5, 2, 1.25, false, "t") };

        var card = await CardAsync(f);

        Status(card, 3).ShouldBe("fail");
        card.Rows.Single(r => r.Number == 3).Threshold.ShouldContain("0.375");
    }

    [Fact]
    public async Task A_forward_series_gross_of_charges_is_not_offered_as_net()
    {
        var f = new ReadinessFixture { Confirmed = false };

        var card = await CardAsync(f);

        card.SubScores.Single(s => s.Name == SubScore.EffectName).State.ShouldBe("unavailable");
        card.HardFails.ShouldContain(ReadinessCalculator.CostSheetName);
        card.Notes.ShouldContain(n => n.Contains("gross of charges", StringComparison.Ordinal));
        Status(card, 15).ShouldBe("unavailable");
    }

    [Fact]
    public async Task The_cf09_failure_boundary_is_a_hard_fail_named()
    {
        var f = new ReadinessFixture { SequentialResult = Result.Success(ReadinessFixture.SequentialView("failure", 120)) };

        var card = await CardAsync(f);

        card.Score.ShouldBe(0);
        card.HardFails.ShouldContain(ReadinessCalculator.FailureBoundaryName);
        card.Label.ShouldBe("not_ready");
    }

    [Fact]
    public async Task An_unreadable_sequential_test_is_unavailable_and_so_a_hard_fail()
    {
        var f = new ReadinessFixture { SequentialResult = Result.Failure<SequentialStatusView>(Error.Validation("x", "bad")) };

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.FailureBoundaryName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_drift_alarm_takes_the_stability_sub_score_to_zero_and_a_cusum_that_is_not_judged_is_unavailable()
    {
        var alarm = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView(alarm: true)) };
        var notJudged = new ReadinessFixture();
        notJudged.DecayResult = Result.Success(ReadinessFixture.DecayView() with { Edge = ReadinessFixture.DecayView().Edge with { Cusum = null } });

        (await CardAsync(alarm)).SubScores.Single(s => s.Name == SubScore.StabilityName).Score.ShouldBe(0);
        (await CardAsync(notJudged)).SubScores.Single(s => s.Name == SubScore.StabilityName).State.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_paused_cell_of_an_applied_kill_proposal_is_a_hard_fail_and_a_re_enabled_one_is_not()
    {
        const string cell = "ORB|15m|BUY|intraday|FIXED_RR|*";
        var f = new ReadinessFixture();
        f.KillsResult = Result.Success(new KillProposalsView([new KillProposalView(1, "ORB", "decay_twice", false, null, null, 100, "t", [cell], "h", new DateOnly(2026, 11, 1), DateTimeOffset.UnixEpoch, "applied", "owner:me@x.test", DateTimeOffset.UnixEpoch, null)], new KillSettingsView(false, null, 1.5, 2, "h"), []));
        await Walk(f, cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var paused = await CardAsync(f);
        await Walk(f, cell, CellState.Paper);
        var reEnabled = await CardAsync(f);

        paused.HardFails.ShouldContain(ReadinessCalculator.KillName);
        reEnabled.HardFails.ShouldNotContain(ReadinessCalculator.KillName);
    }

    [Fact]
    public async Task A_champion_model_makes_the_calibration_sub_score_apply_with_its_error()
    {
        var f = new ReadinessFixture { Champions = [ReadinessFixture.Champion(6)] };

        var card = await CardAsync(f);

        var calibration = card.SubScores.Single(s => s.Name == SubScore.CalibrationName);
        calibration.State.ShouldBe("scored");
        calibration.Score.ShouldBe(40);
    }

    [Fact]
    public async Task The_forward_mean_inside_the_five_percent_band_is_inside_and_far_below_it_is_below()
    {
        var inside = new ReadinessFixture();
        var below = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView() with { Reference = new DecayReferenceView("available", null, 400, 1.5, 0.5, "net") }) };

        (await CardAsync(inside)).SubScores.Single(s => s.Name == SubScore.AgreementName).Score.ShouldBe(100, "the months are positive in the fixture's series");
        (await CardAsync(below)).SubScores.Single(s => s.Name == SubScore.AgreementName).Score.ShouldBe(0);
    }

    [Fact]
    public async Task No_net_reference_leaves_the_agreement_unavailable()
    {
        var f = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView() with { Reference = new DecayReferenceView("unavailable", "none", null, null, null, null) }) };

        var card = await CardAsync(f);

        card.SubScores.Single(s => s.Name == SubScore.AgreementName).State.ShouldBe("unavailable");
        card.Score.ShouldBe(0);
    }

    [Fact]
    public async Task The_phase_check_is_read_once_for_any_number_of_cards()
    {
        var f = new ReadinessFixture();
        var forward = f.ForwardReader;

        await forward.ReadAsync("ORB", Ct);
        await forward.ReadAsync("ORB", Ct);

        await f.Phase.ReceivedWithAnyArgs(1).HandleAsync(default!, Ct);
    }

    [Theory]
    [InlineData("MET", 15)]
    [InlineData("NOT MET", 14)]
    public async Task Row_15_follows_the_phase_check_verdict(string verdict, int rowsPassed)
    {
        var f = new ReadinessFixture();
        f.PhaseResult = Result.Success(new PaperPhaseCheckView(new XDrishti.Domain.Paper.PaperPhaseReport(null, default, 0, [], verdict, verdict, [], [], null!, []), "t"));

        var card = await CardAsync(f);

        card.RowsPassed.ShouldBe(rowsPassed);
    }

    [Fact]
    public async Task The_settings_hash_and_text_name_the_thresholds_used()
    {
        var f = new ReadinessFixture { Options = new ReadinessOptions(MinNEff: 400) };

        var card = await CardAsync(f);

        card.SettingsHash.ShouldBe(f.Options.SnapshotHash());
        card.Settings.ShouldContain("nEff=400");
        Status(card, 2).ShouldBe("fail", "n_eff is 320 in the verdict, which is under the raised bar of 400");
    }

    // ---- row 13: the benchmark comparison on the Lab verdict (SC-S4b, ADR 0227) ----

    private static string RowValue(ScorecardView card, int row) => card.Rows.Single(r => r.Number == row).Value;

    [Fact]
    public async Task Row_13_passes_only_when_the_strategys_daily_sharpe_is_strictly_greater()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Benchmark = new BenchmarkComparisonView(250, 20, 0.12, 0.05, string.Empty, string.Empty) };
        Status(await CardAsync(f), 13).ShouldBe("pass");

        f.Verdict = f.Verdict with { Benchmark = f.Verdict.Benchmark! with { StrategySharpe = 0.05 } };
        var equal = await CardAsync(f);
        Status(equal, 13).ShouldBe("fail", "an equal Sharpe does not beat the hold");
        equal.GateS.Passed.ShouldBeFalse();

        f.Verdict = f.Verdict with { Benchmark = f.Verdict.Benchmark! with { StrategySharpe = 0.0499999 } };
        Status(await CardAsync(f), 13).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_13_is_unavailable_when_the_block_has_no_figures_and_says_why()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Benchmark = new BenchmarkComparisonView(0, 20, null, null, "3 of 20 members cannot be used for the buy-and-hold", string.Empty) };

        var card = await CardAsync(f);

        Status(card, 13).ShouldBe("unavailable");
        card.Notes.Concat(card.Rows.Select(r => r.Value)).ShouldContain(n => n.Contains("3 of 20 members", StringComparison.Ordinal));
        card.Waiting.ShouldContain(w => w.Contains("row 13", StringComparison.Ordinal));
        card.GateS.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Row_13_is_unavailable_when_one_sharpe_is_missing()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Benchmark = new BenchmarkComparisonView(250, 20, 0.12, null, "no variance", string.Empty) };

        Status(await CardAsync(f), 13).ShouldBe("unavailable");
    }

    [Fact]
    public async Task Row_13_is_unavailable_for_a_verdict_stored_before_the_comparison_existed()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Benchmark = null };

        var card = await CardAsync(f);

        Status(card, 13).ShouldBe("unavailable");
        RowValue(card, 13).ShouldContain("before the benchmark comparison existed");
    }

    [Fact]
    public async Task Row_13_is_unavailable_when_the_strategy_has_no_lab_run()
    {
        var f = new ReadinessFixture { Verdict = null };

        var card = await CardAsync(f);

        Status(card, 13).ShouldBe("unavailable");
        card.Waiting.ShouldContain(w => w.Contains("row 13", StringComparison.Ordinal));
    }

    private static async Task Walk(ReadinessFixture f, string cell, params CellState[] states)
    {
        foreach (var state in states)
        {
            var move = new Application.Learning.RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:x"]);
            var handler = new Application.Learning.RecordCellTransitionHandler(f.Log, new FakeReadinessGate(), f.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Learning.RecordCellTransitionHandler>.Instance);
            (await handler.HandleAsync(move, Ct)).IsSuccess.ShouldBeTrue();
        }
    }
}

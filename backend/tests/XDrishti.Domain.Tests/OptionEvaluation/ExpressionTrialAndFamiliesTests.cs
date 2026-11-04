using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Domain.Tests.OptionEvaluation;

/// <summary>P14-09: the family list of its own, the trial identity (hash of family and text, run id over the window with the p14 prefix), the OOS windows and the primary profile.</summary>
public sealed class ExpressionTrialAndFamiliesTests
{
    private static readonly string Profile = new('a', 64);
    private static readonly string Run = new('b', 64);

    [Fact]
    public void The_expression_family_is_known_to_its_list_and_not_to_the_p15_or_pairs_lists()
    {
        ExpressionFamilies.IsKnown("expr:option_buy").ShouldBeTrue();
        ExpressionFamilies.All.ShouldBe(["expr:option_buy", "expr:debit_spread", "expr:credit_spread", "expr:iron_condor"]); // P14-10: each structure is its own family
        ExtensionFamilies.IsKnown("expr:option_buy").ShouldBeFalse();
        ExtensionFamilies.All.ShouldNotContain("expr:option_buy");
        ExtensionFamilies.All.Concat(PairFamilies.All).ShouldNotContain(f => f.StartsWith("expr:", StringComparison.Ordinal));
        PairFamilies.IsKnown("expr:option_buy").ShouldBeFalse();
        (ExpressionFamilies.Protocol, ExpressionFamilies.VersionKey).ShouldBe(("O", "expr-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expr:option_sell")]
    [InlineData("EXPR:OPTION_BUY")]
    [InlineData("pairs:intraday-1")]
    public void Any_other_text_is_not_an_expression_family(string? family)
    {
        ExpressionFamilies.IsKnown(family).ShouldBeFalse();
    }

    [Fact]
    public void The_trial_hash_depends_on_the_profile_and_the_run_and_not_on_the_window_and_the_run_id_does()
    {
        var a = ExpressionTrial.Of("expr:option_buy", Profile, Run, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));
        var sameOtherWindow = ExpressionTrial.Of("expr:option_buy", Profile, Run, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1));
        var otherProfile = ExpressionTrial.Of("expr:option_buy", new string('c', 64), Run, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));
        var otherRun = ExpressionTrial.Of("expr:option_buy", Profile, new string('d', 64), new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));

        sameOtherWindow.Hash.ShouldBe(a.Hash);
        sameOtherWindow.RunId.ShouldNotBe(a.RunId);
        otherProfile.Hash.ShouldNotBe(a.Hash);
        otherRun.Hash.ShouldNotBe(a.Hash);
        a.Hash.Length.ShouldBe(64);
        a.Text.ShouldContain("profile:" + Profile);
        ExpressionTrial.Of("expr:option_buy", Profile, Run, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)).RunId.ShouldBe(a.RunId);
    }

    [Fact]
    public void The_run_id_has_the_p14_prefix_and_not_the_p15_one()
    {
        var trial = ExpressionTrial.Of("expr:option_buy", Profile, Run, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));
        var p15 = ExtensionFamilies.TrialOf("expr:option_buy", ExpressionTrial.Text(Profile, Run), new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));

        p15.Hash.ShouldBe(trial.Hash);
        p15.RunId.ShouldNotBe(trial.RunId);
        ExpressionTrial.RunIdPrefix.ShouldBe("p14-ledger/1");
    }

    [Fact]
    public void The_windows_are_the_walk_forward_geometry_per_side_and_the_cut_parts_do_not_overlap_the_hold_out()
    {
        var windows = ExpressionWindows.Of([new SideBounds("CE", new DateOnly(2022, 1, 3), new DateOnly(2025, 12, 30)), new SideBounds("PE", new DateOnly(2024, 6, 3), new DateOnly(2025, 12, 1))]);

        windows.Where(w => w.Side == "CE").First().Start.ShouldBe(new DateOnly(2022, 11, 1));
        windows.Where(w => w.Side == "CE").ShouldAllBe(w => w.End >= w.Start);
        windows.Where(w => w.Side == "PE").First().Start.ShouldBe(new DateOnly(2025, 4, 1)); // nine months after the first entry, to a month start
        var cutAt = new DateOnly(2025, 5, 15);
        ExpressionWindows.Before(windows, cutAt).ShouldAllBe(w => w.End < cutAt);
        ExpressionWindows.From(windows, cutAt).ShouldAllBe(w => w.Start >= cutAt && w.End >= cutAt);
        ExpressionWindows.Before(windows, null).ShouldBe(windows);
        ExpressionWindows.Of([]).ShouldBeEmpty();
    }

    [Fact]
    public void The_primary_profile_hash_is_stable_and_the_latest_epoch_defaults_to_the_spec_date()
    {
        ExpressionProfiles.PrimaryHash.Length.ShouldBe(64);
        ExpressionProfiles.PrimaryHash.ShouldBe(ExpressionProfiles.PrimaryHash);
        ExpressionEpochs.LatestFrom([]).ShouldBe(new DateOnly(2025, 9, 1));
        ExpressionEpochs.LatestFrom([new XDrishti.Domain.Options.MarketEpoch("nifty_expiry_tuesday", new DateOnly(2025, 9, 2), "x", null, true)]).ShouldBe(new DateOnly(2025, 9, 2));
    }

    [Fact]
    public void The_pair_reads_its_flags_from_the_row_and_its_date_from_the_india_day_of_the_entry()
    {
        var entry = new DateTimeOffset(2025, 9, 1, 20, 0, 0, TimeSpan.Zero); // 01:30 on 2 September in India
        var pair = ExpressionPair.Of("K", entry, entry.AddMinutes(30), 1.5, 1.0, true, "derived", "sizable", ["bs_fallback", "spread_from_recent_archive"]);

        pair.Date.ShouldBe(new DateOnly(2025, 9, 2));
        (pair.BsFallback, pair.SpreadFromRecentArchive, pair.ExpiryDerived, pair.Sizable, pair.Net).ShouldBe((true, true, true, true, true));
        pair.D.ShouldBe(0.5, 1e-12);
        var plain = ExpressionPair.Of("K", entry, entry, 1, 1, false, "contract", "not_sizable", []);
        (plain.BsFallback, plain.SpreadFromRecentArchive, plain.ExpiryDerived, plain.Sizable, plain.Net).ShouldBe((false, false, false, false, false));
    }
}

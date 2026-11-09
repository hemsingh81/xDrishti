using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>AU-10 criteria 1, 6, 7, 8: the pure rule. Hand-worked tables, and a property: an automatic change never moves the unsafe way.</summary>
public sealed class LearnedSettingProposerTests
{
    private static readonly FakeLearnedConfig Config = new();

    private static LearnedEvidenceItem Evidence(string key, string proposed, bool oos = true, bool sealedHoldOut = false, string hash = "h1", string reference = "paper fills of weeks 40 to 45") =>
        new(key, proposed, reference, hash, oos, sealedHoldOut);

    [Theory]
    [InlineData("4", "4.2", 10, "4.2", LearnedOutcome.Tighten)] // within the step of 0.4: as asked
    [InlineData("4", "5", 10, "4.4", LearnedOutcome.Tighten)] // a step is 10% of the value in force: 4 + 0.4
    [InlineData("4", "5", 25, "5", LearnedOutcome.Tighten)] // 25% allows the whole move, and 5 is the bound
    [InlineData("4", "3.9", 10, "3.9", LearnedOutcome.Loosen)] // lower on a higher-is-safer key: a proposal, never automatic
    [InlineData("4", "3", 10, "3", LearnedOutcome.Loosen)]
    public void Tighten_and_loosen_table_for_a_higher_is_safer_key(string current, string proposed, int step, string expected, LearnedOutcome outcome)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), current, Evidence(FakeLearnedConfig.BpsKey, proposed), step, current, 100);

        d.Outcome.ShouldBe(outcome);
        LearnedSettingProposer.Text(d.NewValue).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1", "0.5", "0.9", LearnedOutcome.Tighten)] // lower is safer; the step is 0.1
    [InlineData("1", "0.95", "0.95", LearnedOutcome.Tighten)]
    [InlineData("1", "1.2", "1.2", LearnedOutcome.Loosen)]
    public void Tighten_and_loosen_table_for_a_lower_is_safer_key(string current, string proposed, string expected, LearnedOutcome outcome)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor("Confidence:Decay:Sigmas"), current, Evidence("Confidence:Decay:Sigmas", proposed), 10, current, 100);

        d.Outcome.ShouldBe(outcome);
        LearnedSettingProposer.Text(d.NewValue).ShouldBe(expected);
    }

    [Fact]
    public void A_key_with_no_declared_direction_is_never_learnable()
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.NoDirectionKey), "50", Evidence(FakeLearnedConfig.NoDirectionKey, "60"), 10, "50", 100);

        d.Outcome.ShouldBe(LearnedOutcome.Skip);
        d.Code.ShouldBe("learned.not_learnable");
    }

    [Fact]
    public void Loop_level_is_not_learnable_even_with_a_direction()
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.LoopLevel), "0.95", Evidence(FakeLearnedConfig.LoopLevel, "0.97"), 10, "0.95", 100);

        d.Outcome.ShouldBe(LearnedOutcome.Skip);
        d.Code.ShouldBe("learned.not_learnable");
    }

    [Fact]
    public void A_learnable_flag_alone_is_not_enough_the_key_must_be_on_the_allow_list()
    {
        var rogue = Config.Descriptor(FakeLearnedConfig.NoDirectionKey)! with { Learnable = true, Safer = SettingSafer.Higher };

        LearnedSettingProposer.Propose(rogue, "50", Evidence(rogue.Key, "55"), 10, "50", 100).Code.ShouldBe("learned.not_learnable");
    }

    [Theory]
    [InlineData(false, false, "learned.not_out_of_sample")]
    [InlineData(true, true, "learned.sealed_evidence")] // hold-out data is never read here, even when it is out of sample
    public void Evidence_that_is_not_out_of_sample_or_is_sealed_changes_nothing(bool oos, bool sealedHoldOut, string code)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), "4", Evidence(FakeLearnedConfig.BpsKey, "4.2", oos, sealedHoldOut), 10, "4", 100);

        d.Outcome.ShouldBe(LearnedOutcome.Skip);
        d.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(null, "4.2", "h", "r")]
    [InlineData("4", "not a number", "h", "r")]
    [InlineData("4", "4.2", "", "r")]
    [InlineData("4", "4.2", "h", " ")]
    public void Missing_or_unreadable_inputs_never_count_as_a_pass(string? current, string proposed, string hash, string reference)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), current, Evidence(FakeLearnedConfig.BpsKey, proposed, hash: hash, reference: reference), 10, current, 100);

        d.Outcome.ShouldBe(LearnedOutcome.Skip);
        d.Code.ShouldBe("learned.evidence_unavailable");
    }

    [Fact]
    public void No_evidence_is_a_skip()
    {
        LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), "4", null, 10, "4", 100).Code.ShouldBe("learned.evidence_unavailable");
    }

    [Theory]
    [InlineData("4", "6", "learned.out_of_range")]
    [InlineData("4", "4", "learned.no_change")]
    public void A_value_outside_the_bounds_or_no_change_is_left_alone(string current, string proposed, string code)
    {
        LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), current, Evidence(FakeLearnedConfig.BpsKey, proposed), 10, current, 100).Code.ShouldBe(code);
    }

    [Fact]
    public void An_automatic_change_always_moves_in_the_safer_direction_over_many_inputs()
    {
        var random = new Random(20261109);
        foreach (var key in new[] { FakeLearnedConfig.BpsKey, "Confidence:Decay:Sigmas" })
        {
            var d = Config.Descriptor(key)!;
            for (var i = 0; i < 2000; i++)
            {
                var current = (decimal)random.NextDouble() * ((d.Max ?? 5) - (d.Min ?? 0)) + (d.Min ?? 0);
                var proposed = (decimal)random.NextDouble() * ((d.Max ?? 5) - (d.Min ?? 0)) + (d.Min ?? 0);
                var step = random.Next(1, 26);

                var decision = LearnedSettingProposer.Propose(d, LearnedSettingProposer.Text(current), Evidence(key, LearnedSettingProposer.Text(proposed)), step, LearnedSettingProposer.Text(current), 100);

                if (decision.Outcome == LearnedOutcome.Tighten)
                {
                    d.IsLoosening(decimal.Parse(LearnedSettingProposer.Text(current), System.Globalization.CultureInfo.InvariantCulture), decision.NewValue).ShouldBeFalse($"{key}: {current} -> {decision.NewValue}");
                    d.InRange(decision.NewValue).ShouldBeTrue();
                }
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    public void Without_a_readable_configured_value_nothing_is_proposed(string? configured)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), "4", Evidence(FakeLearnedConfig.BpsKey, "5"), 10, configured, 100);

        d.Outcome.ShouldBe(LearnedOutcome.Skip);
        d.Code.ShouldBe("learned.evidence_unavailable");
    }

    [Theory]
    [InlineData("4.4", true)]
    [InlineData("4.41", false)]
    [InlineData("junk", false)]
    public void The_drift_check_for_a_stored_proposal_fails_closed(string value, bool within) =>
        LearnedSettingProposer.WithinDrift(Config.Descriptor(FakeLearnedConfig.BpsKey), "4", value, 10).ShouldBe(within);

    [Theory]
    [InlineData("4.4", "4", 10, 10, "4.4")] // at the limit already (10% of 4 = 0.4): nothing more
    [InlineData("4.2", "4", 10, 10, "4.4")] // the step would reach 4.62 but the room is 0.4
    public void The_total_limit_stops_the_drift_from_the_configured_value(string current, string configured, int step, int total, string expectedOrLimit)
    {
        var d = LearnedSettingProposer.Propose(Config.Descriptor(FakeLearnedConfig.BpsKey), current, Evidence(FakeLearnedConfig.BpsKey, "5"), step, configured, total);

        if (current == expectedOrLimit)
        {
            d.Code.ShouldBe("learned.total_limit");
        }
        else
        {
            LearnedSettingProposer.Text(d.NewValue).ShouldBe(expectedOrLimit);
        }
    }
}

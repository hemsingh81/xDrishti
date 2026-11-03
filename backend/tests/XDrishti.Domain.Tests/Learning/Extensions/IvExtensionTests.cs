using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P14-11: the catalog and the group of <c>iv-1</c>, the options preset of the comparison, the extraction record's sources and the family kept apart from the P15 list.</summary>
public sealed class IvExtensionTests
{
    [Fact]
    public void The_group_is_iv_with_the_version_iv_1_and_fits_the_table_patterns()
    {
        IvFeatureCatalog.Group.Id.ShouldBe("iv");
        IvFeatureCatalog.Group.Version.ShouldBe("iv-1");
        IvFeatureCatalog.Group.Problem().ShouldBeNull();
        ExtensionCatalogs.GroupOf("iv-1").ShouldBeSameAs(IvFeatureCatalog.Group);
        ExtensionCatalogs.GroupOf("iv-2").ShouldBeNull();
        ExtensionFamilies.ExtensionVersionOf(IvFeatureCatalog.Family).ShouldBe("iv-1");
    }

    [Fact]
    public void The_family_is_not_in_the_p15_list_the_phase_check_iterates()
    {
        ExtensionFamilies.All.ShouldNotContain(IvFeatureCatalog.Family);
        ExtensionFamilies.IsKnown(IvFeatureCatalog.Family).ShouldBeFalse();
    }

    [Fact]
    public void The_option_feature_families_hold_the_iv_family_and_are_apart_from_the_p15_list()
    {
        OptionFeatureFamilies.All.ShouldBe(["ext:iv-1"]);
        OptionFeatureFamilies.IsKnown("ext:iv-1").ShouldBeTrue();
        OptionFeatureFamilies.IsKnown("ext:ev-1").ShouldBeFalse();
        OptionFeatureFamilies.IsKnown(null).ShouldBeFalse();
        IvReasons.All.ShouldContain(IvReasons.NoEligibleSeries);
    }

    [Fact]
    public void The_options_preset_is_the_depth_preset_on_quarterly_folds_with_twelve_months_of_training_and_a_five_session_embargo()
    {
        var options = ExtensionComparisonOptions.ForOptions();
        var depth = ExtensionComparisonOptions.ForDepth();

        (options.Unit, options.MinTrainMonths, options.EmbargoSessions).ShouldBe((FoldUnit.Quarter, 12, 5));
        (options.Block, options.BlockSessions, options.SensitivityBlockSessions).ShouldBe((BlockKind.Sessions, 1, 5));
        (options.MinPairedCandidates, options.MinFlips, options.MaxDeltaLogLoss, options.MaxDeltaBrier).ShouldBe((2_000, 300, -0.005, -0.002));
        (options.MinEvaluableFolds, options.MinGainR, options.Draws).ShouldBe((depth.MinEvaluableFolds, depth.MinGainR, depth.Draws));
    }

    [Fact]
    public void The_protocol_takes_the_options_preset_for_iv_1_and_the_filings_preset_for_the_others()
    {
        var iv = ProtocolX.OptionsFor("iv-1", new string('a', 64), "label-1", new DateOnly(2026, 1, 1));
        var ev = ProtocolX.OptionsFor("ev-1", new string('a', 64), "label-1", new DateOnly(2026, 1, 1));

        (iv.Unit, iv.Block, iv.MinFlips, iv.HoldOutStart).ShouldBe((FoldUnit.Quarter, BlockKind.Sessions, 300, new DateOnly(2026, 1, 1)));
        (ev.Unit, ev.Block, ev.MinFlips).ShouldBe((FoldUnit.Quarter, BlockKind.Season, 0));
        iv.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_sources_are_written_after_the_counts_read_back_and_merged_over_runs()
    {
        FeatureCell[][] rows = [[FeatureCell.Known(1), FeatureCell.Unknown(IvReasons.NoOptions)]];
        var json = ExtensionNanCounts.Json(["a", "b"], rows, IvSourceNotes.Of(42, [3, 1, 2, 2], "percent_assumed"));

        json.ShouldBe("{\"nan\":{\"a\":{},\"b\":{\"no_options\":1}},\"parser_unverified\":{\"a\":0,\"b\":0},\"sources\":{\"archive_watermark\":\"42\",\"iv_unit\":\"percent_assumed\",\"quality_run_ids\":\"1,2,3\"}}");
        ExtensionGate.ParserUnverifiedCount(json).ShouldBe(0, "the gate still reads the counts");
        var read = IvSourceNotes.Read(json);
        read["archive_watermark"].ShouldBe("42");
        var other = IvSourceNotes.Of(50, [3, 9], "percent_assumed");

        var merged = IvSourceNotes.Merge([read, other]);

        merged["archive_watermark"].ShouldBe("50");
        merged["quality_run_ids"].ShouldBe("1,2,3,9");
        merged["iv_unit"].ShouldBe("percent_assumed");
    }

    [Fact]
    public void A_record_without_sources_merges_to_unavailable_never_to_zero()
    {
        var merged = IvSourceNotes.Merge([IvSourceNotes.Read("{\"nan\":{},\"parser_unverified\":{}}"), IvSourceNotes.Read("not json")]);

        merged["archive_watermark"].ShouldBe(ExtensionJson.Unavailable);
        merged["quality_run_ids"].ShouldBe("none");
        merged["iv_unit"].ShouldBe(ExtensionJson.Unavailable);
    }

    [Fact]
    public void The_nan_counts_without_sources_are_byte_identical_to_the_p15_format()
    {
        FeatureCell[][] rows = [[FeatureCell.Unknown(FeatureReasons.NoFiling)]];

        ExtensionNanCounts.Json(["a"], rows).ShouldBe("{\"nan\":{\"a\":{\"no_filing\":1}},\"parser_unverified\":{\"a\":0}}");
    }
}

using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>P6-02 (ADR 0146): a regime label exists only where the context returned a regime; otherwise it says why; names and cell parts.</summary>
public sealed class RegimeLabelTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    private static ContextFigures Figures(double? regime, string basis = ContextBasis.IndexAndVix) =>
        new(Day.AddDays(-1), basis, 1, 20, 50, 15, 40, 0, 1, 0, regime, null);

    [Fact]
    public void A_ready_regime_is_a_label_with_its_name_basis_and_the_day_it_was_read_from()
    {
        var label = RegimeLabel.Of(Day, ContextRead.Of(Figures(3)));

        label.IsAvailable.ShouldBeTrue();
        label.Code.ShouldBe(3);
        label.Name.ShouldBe("UP_HIGH_VOL");
        label.Basis.ShouldBe(ContextBasis.IndexAndVix);
        label.ContextAsOf.ShouldBe(Day.AddDays(-1));
        label.Unavailable.ShouldBeNull();
        label.CellPart.ShouldBe("UP_HIGH_VOL");
    }

    [Fact]
    public void A_label_built_from_the_index_alone_has_a_different_cell_part()
    {
        var label = RegimeLabel.Of(Day, ContextRead.Of(Figures(9, ContextBasis.IndexOnly)));

        label.CellPart.ShouldBe("DOWN_HIGH_VOL~index_only");
        RegimeNames.ParsePart(label.CellPart).ShouldBe(("DOWN_HIGH_VOL", ContextBasis.IndexOnly));
        RegimeNames.ParsePart("DOWN_HIGH_VOL").ShouldBe(("DOWN_HIGH_VOL", ContextBasis.IndexAndVix));
    }

    [Theory]
    [InlineData(ContextRead.NoContext)]
    [InlineData(ContextRead.BeforeHistory)]
    [InlineData(ContextRead.Stale)]
    [InlineData(ContextRead.Invalid)]
    public void Every_reason_of_the_context_is_passed_on_and_there_is_no_code_name_or_basis(string reason)
    {
        var label = RegimeLabel.Of(Day, ContextRead.Not(reason));

        label.IsAvailable.ShouldBeFalse();
        label.Unavailable.ShouldBe(reason);
        label.Code.ShouldBeNull();
        label.Name.ShouldBeNull();
        label.Basis.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => _ = label.CellPart);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public void A_day_whose_regime_is_not_ready_or_not_a_code_is_regime_not_ready_never_a_default(double? regime)
    {
        var label = RegimeLabel.Of(Day, ContextRead.Of(Figures(regime)));

        label.IsAvailable.ShouldBeFalse();
        label.Unavailable.ShouldBe(RegimeLabel.RegimeNotReady);
        label.ContextAsOf.ShouldBe(Day.AddDays(-1));
    }

    [Fact]
    public void The_nine_names_are_in_the_order_of_their_codes_and_unknown_spellings_have_no_code()
    {
        RegimeNames.All.Count.ShouldBe(9);
        for (var code = 1; code <= 9; code++)
        {
            RegimeNames.CodeOf(RegimeNames.NameOf(code)).ShouldBe(code);
        }

        RegimeNames.CodeOf("up_low_vol").ShouldBeNull();
        RegimeNames.CodeOf(null).ShouldBeNull();
        RegimeNames.ParsePart("*").ShouldBeNull();
        RegimeNames.ParsePart("UP_LOW_VOL~other").ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => RegimeNames.NameOf(0));
    }

    [Fact]
    public void The_tally_counts_labels_by_basis_and_the_missing_by_reason()
    {
        var labels = new[]
        {
            RegimeLabel.Of(Day, ContextRead.Of(Figures(1))),
            RegimeLabel.Of(Day, ContextRead.Of(Figures(2, ContextBasis.IndexOnly))),
            RegimeLabel.Of(Day, ContextRead.Not(ContextRead.Stale)),
            RegimeLabel.Of(Day, ContextRead.Not(ContextRead.Stale)),
            RegimeLabel.Of(Day, ContextRead.Of(Figures(null))),
        };

        var tally = RegimeTally.Of(labels);

        (tally.Labelled, tally.IndexAndVix, tally.IndexOnly, tally.Unavailable, tally.Total).ShouldBe((2, 1, 1, 3, 5));
        tally.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["regime_not_ready"] = 1, ["stale"] = 2 });
    }
}

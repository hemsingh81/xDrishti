using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>The small values of the replay: options, counts, fates, the record of an instrument that was not found, and the canonical text.</summary>
public sealed class ReplayValuesTests
{
    [Fact]
    public void The_default_options_are_090_and_020_and_valid()
    {
        ReplayOptions.Default.ShouldBe(new ReplayOptions(0.90, 0.20));
        ReplayOptions.Default.Problem().ShouldBeNull();
        new ReplayOptions().ShouldBe(ReplayOptions.Default);
    }

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(-0.1, 0.2)]
    [InlineData(1.0000001, 0.2)]
    [InlineData(double.NaN, 0.2)]
    [InlineData(double.PositiveInfinity, 0.2)]
    [InlineData(0.9, 0.0)]
    [InlineData(0.9, -1.0)]
    [InlineData(0.9, double.NaN)]
    [InlineData(0.9, double.PositiveInfinity)]
    public void Options_out_of_range_name_the_problem(double completeness, double threshold)
    {
        new ReplayOptions(completeness, threshold).Problem().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(1.0, 1e-300)]
    [InlineData(1e-300, 1e300)]
    public void Options_at_their_limits_are_valid(double completeness, double threshold)
    {
        new ReplayOptions(completeness, threshold).Problem().ShouldBeNull();
    }

    [Fact]
    public void The_record_of_an_instrument_that_was_not_found_has_only_its_id_and_the_skip()
    {
        var id = Guid.NewGuid();

        var record = InstrumentRun.NotFound(id);

        record.InstrumentId.ShouldBe(id);
        record.Symbol.ShouldBe("?");
        record.Skipped.ShouldBe(SkipReason.InstrumentNotFound);
        record.BarsSha256.ShouldBeNull();
        (record.FirstBar, record.LastBar, record.ReplayFrom, record.CountersCover, record.TickSize).ShouldBe((null, null, null, null, null));
        (record.BarsRead, record.BarsFed, record.BarsExcluded, record.CandidatesInWindow, record.TradesInWindow).ShouldBe((0, 0, 0, 0, 0));
        record.Sessions.ShouldBe(SessionCounts.Empty);
        record.Fates.ShouldBe(RunFates.Empty);
        record.Walk.ShouldBe(WalkCounters.Empty);
        record.Scans.ShouldBeEmpty();
        record.Notes.ShouldBeEmpty();
        record.Adjustments.ShouldBeEmpty();
        record.Discontinuities.ShouldBeEmpty();
    }

    [Fact]
    public void Session_counts_add_field_by_field_and_know_what_was_excluded()
    {
        var sum = new SessionCounts(5, 1, 2, 3, 4).Add(new SessionCounts(1, 10, 20, 30, 40));

        sum.ShouldBe(new SessionCounts(6, 11, 22, 33, 44));
        sum.Excluded.ShouldBe(110);
        SessionCounts.Empty.Add(sum).ShouldBe(sum);
        sum.Of(SessionExclusion.ShortSession).ShouldBe(33);
    }

    [Fact]
    public void Fates_add_field_by_field_and_total_every_fate()
    {
        var a = new RunFates(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        a.Total.ShouldBe(55);
        a.NoTrade.ShouldBe(36);
        a.Add(a).ShouldBe(new RunFates(2, 4, 6, 8, 10, 12, 14, 16, 18, 20));
        RunFates.Empty.Add(a).ShouldBe(a);
        Should.Throw<ArgumentNullException>(() => a.Add(null!));
    }

    [Fact]
    public void Canonical_numbers_do_not_depend_on_the_culture_the_scale_or_the_sign_of_a_zero()
    {
        var before = CultureInfo.CurrentCulture;
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = comma;

            CanonicalText.Number(0.1).ShouldBe("0.1");
            CanonicalText.Number(-2.5).ShouldBe("-2.5");
            CanonicalText.Number(0.05m).ShouldBe("0.05");
            CanonicalText.Number(12L).ShouldBe("12");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }

        CanonicalText.Number(-0.0).ShouldBe("0");
        CanonicalText.Number(0.0).ShouldBe("0");
        CanonicalText.Number(1e-7).ShouldBe("1E-07");
        CanonicalText.Number(0.1 + 0.2).ShouldBe("0.30000000000000004");
        CanonicalText.Number(5.0).ShouldBe("5");
    }

    [Theory]
    [InlineData("0.05")]
    [InlineData("0.0500")]
    [InlineData("0.050000")]
    public void A_decimal_is_written_without_its_trailing_zeros_whatever_its_scale(string text)
    {
        CanonicalText.Number(decimal.Parse(text, CultureInfo.InvariantCulture)).ShouldBe("0.05");
        CanonicalText.Number(0.00m).ShouldBe("0");
        CanonicalText.Number(1.00m).ShouldBe("1");
        CanonicalText.Number(0.0001m).ShouldBe("0.0001");
        CanonicalText.Number(-0.0m).ShouldBe("0");
    }

    [Fact]
    public void The_sha256_of_a_text_is_lower_case_hex_of_its_utf8_bytes()
    {
        CanonicalText.Sha256Hex("").ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        CanonicalText.Sha256Hex("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        CanonicalText.Sha256Hex("é").ShouldBe("4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c");
        Should.Throw<ArgumentNullException>(() => CanonicalText.Sha256Hex(null!));
    }
}

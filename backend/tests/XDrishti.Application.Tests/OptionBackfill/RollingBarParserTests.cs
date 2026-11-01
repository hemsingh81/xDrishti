using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>Arrays to rows (P14-03 criterion 4): lengths, the side that matches, paise, the suspect flag, the hours, nulls never zero. Every shape is UNVERIFIED until the probe.</summary>
public sealed class RollingBarParserTests
{
    private static RollingParse Parse(byte[] body, string side = "CE", int? step = 5_000) => RollingBarParser.Parse(body, Item(side, step: step));

    [Fact]
    public void The_documented_sample_file_parses_into_three_bars_in_paise()
    {
        using var stream = typeof(RollingBarParserTests).Assembly.GetManifestResourceStream("DhanSamples/rollingoption-success.json") ?? throw new InvalidOperationException("No sample");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var body = memory.ToArray();

        var parsed = RollingBarParser.Parse(body, Item("CE"));

        parsed.Outcome.ShouldBe(BackfillOutcomes.Ok);
        parsed.Bars.Select(b => b.Ts).ShouldBe([At(12, 9, 15), At(12, 9, 20), At(12, 9, 25)]);
        var first = parsed.Bars[0];
        (first.StrikePx, first.SpotPx, first.OpenPx, first.HighPx, first.LowPx, first.ClosePx, first.Volume, first.Oi, first.Iv).ShouldBe((2_550_000, 2_501_230, 4_820, 5_230, 4_790, 5_105, 128_450L, 1_523_000L, 12.31f));
    }

    [Fact]
    public void Three_good_bars_become_rows_with_the_request_key_and_paise_half_away_from_zero()
    {
        var parsed = Parse(Body("CE"));

        parsed.Outcome.ShouldBe(BackfillOutcomes.Ok);
        parsed.Bars.Select(b => b.Ts).ShouldBe([At(12, 9, 15), At(12, 9, 20), At(12, 9, 25)]);
        parsed.Bars.ShouldAllBe(b => b.UnderlyingId == NiftyId && b.ExpiryFlag == 'W' && b.ExpiryCode == 1 && b.Offset == 10 && b.Side == "CE" && b.IntervalMinutes == 5 && b.Flags == 0);
        parsed.Bars[0].SpotPx.ShouldBe(2_501_230);
        parsed.OutsideHours.ShouldBe(0);
        Parse(Text($$$"""{"data":{"ce":{"timestamp":[{{{Epoch(9, 15)}}}],"open":[0.005],"high":[0.015],"low":[0.025],"close":[0.035],"strike":[25500],"spot":[25000.125]},"pe":null}}""")).Bars[0]
            .ShouldSatisfyAllConditions(b => b.OpenPx.ShouldBe(1), b => b.HighPx.ShouldBe(2), b => b.LowPx.ShouldBe(3), b => b.ClosePx.ShouldBe(4), b => b.SpotPx.ShouldBe(2_500_013));
    }

    [Fact]
    public void The_put_side_is_read_from_pe_and_a_ce_when_pe_was_asked_is_a_bad_body()
    {
        Parse(Body("PE"), "PE").Bars.ShouldAllBe(b => b.Side == "PE");
        Parse(Body("CE"), "PE").Outcome.ShouldBe(BackfillOutcomes.BadBody);
        Parse(Text("""{"data":{"ce":{"timestamp":[],"open":[],"high":[],"low":[],"close":[],"strike":[],"spot":[]},"pe":{"timestamp":[],"open":[],"high":[],"low":[],"close":[],"strike":[],"spot":[]}}}""")).Outcome.ShouldBe(BackfillOutcomes.BadBody);
    }

    [Fact]
    public void Both_sides_null_or_no_data_is_an_empty_window_not_a_failure()
    {
        Parse(Text("""{"data":{"ce":null,"pe":null}}""")).ShouldSatisfyAllConditions(p => p.Outcome.ShouldBe(BackfillOutcomes.Ok), p => p.Bars.ShouldBeEmpty());
        Parse(Text("""{"data":null}""")).ShouldSatisfyAllConditions(p => p.Outcome.ShouldBe(BackfillOutcomes.Ok), p => p.Bars.ShouldBeEmpty());
        Parse(Text("""{"data":{}}""")).Bars.ShouldBeEmpty();
    }

    [Fact]
    public void Arrays_of_different_lengths_are_a_bad_body()
    {
        var body = Text($$$"""{"data":{"ce":{"timestamp":[{{{Epoch(9, 15)}}},{{{Epoch(9, 20)}}}],"open":[1,2],"high":[1,2],"low":[1,2],"close":[1],"strike":[1,2],"spot":[1,2]},"pe":null}}""");

        Parse(body).Outcome.ShouldBe(BackfillOutcomes.BadBody);
        Parse(body).Bars.ShouldBeEmpty();
        Parse(Text($$$"""{"data":{"ce":{"timestamp":[{{{Epoch(9, 15)}}}],"open":[1],"high":[1],"low":[1],"close":[1],"strike":[1],"spot":[1],"volume":[1,2]},"pe":null}}""")).Outcome.ShouldBe(BackfillOutcomes.BadBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"data\":3}")]
    [InlineData("{\"data\":{\"ce\":3,\"pe\":null}}")]
    [InlineData("{\"data\":{\"ce\":{\"timestamp\":5},\"pe\":null}}")]
    [InlineData("{\"data\":{\"ce\":{\"open\":[]},\"pe\":null}}")]
    public void Anything_that_is_not_the_documented_shape_is_a_bad_body_and_never_throws(string json)
    {
        Parse(Text(json)).Outcome.ShouldBe(BackfillOutcomes.BadBody);
    }

    [Fact]
    public void A_zero_or_negative_close_or_an_odd_strike_is_kept_with_the_suspect_flag()
    {
        var odd = Parse(Text($$$"""{"data":{"ce":{"timestamp":[{{{Epoch(9, 15)}}},{{{Epoch(9, 20)}}},{{{Epoch(9, 25)}}}],"open":[1,1,1],"high":[1,1,1],"low":[1,1,1],"close":[0,-1,5],"strike":[25500,25500,25500.5],"spot":[1,1,1]},"pe":null}}"""));

        odd.Bars.Select(b => b.Flags).ShouldBe([BackfillFlags.Suspect, BackfillFlags.Suspect, BackfillFlags.Suspect]);
        odd.Bars.Count.ShouldBe(3, "kept, never dropped");
        odd.Bars[0].ClosePx.ShouldBe(0, "never zero-filled, never replaced");
        Parse(Body("CE"), step: null).Bars.ShouldAllBe(b => b.Flags == 0, "no known step: no strike check");
    }

    [Fact]
    public void A_bar_outside_0915_to_1530_india_time_is_dropped_and_counted()
    {
        var parsed = Parse(Body("CE", Epoch(9, 10), Epoch(9, 15), Epoch(15, 30), Epoch(15, 35), Epoch(3, 45)));

        parsed.Bars.Select(b => b.Ts).ShouldBe([At(12, 9, 15), At(12, 15, 30)]);
        parsed.OutsideHours.ShouldBe(3);
    }

    [Fact]
    public void Millisecond_timestamps_are_read_and_unusable_values_drop_the_bar_without_zero_fill()
    {
        var ms = Epoch(9, 15) * 1000;
        var body = Text($$$"""{"data":{"ce":{"timestamp":[{{{ms}}},null,{{{Epoch(9, 20)}}},{{{Epoch(9, 25)}}}],"open":[1,1,null,1],"high":[1,1,1,1],"low":[1,1,1,1],"close":[1,1,1,1],"strike":[25500,25500,25500,25500],"spot":[1,1,1,99999999999],"volume":[null,1,1,1],"oi":[1,1,1,-5],"iv":[null,1,1,1]},"pe":null}}""");

        var parsed = Parse(body);

        parsed.Bars.Select(b => b.Ts).ShouldBe([At(12, 9, 15)]);
        parsed.Unusable.ShouldBe(3);
        (parsed.Bars[0].Volume, parsed.Bars[0].Iv).ShouldBe((null, null), "a value Dhan did not send is null, never zero");
    }

    [Fact]
    public void The_optional_arrays_may_be_absent_and_then_the_values_are_null()
    {
        var parsed = Parse(Text($$$"""{"data":{"ce":{"timestamp":[{{{Epoch(9, 15)}}}],"open":[1],"high":[1],"low":[1],"close":[1],"strike":[25500],"spot":[1]},"pe":null}}"""));

        parsed.Bars.ShouldHaveSingleItem().ShouldSatisfyAllConditions(b => b.Volume.ShouldBeNull(), b => b.Oi.ShouldBeNull(), b => b.Iv.ShouldBeNull());
    }
}

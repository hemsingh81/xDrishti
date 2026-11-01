using System.Text;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.OptionChain;
using static XDrishti.Application.Tests.OptionChain.ChainSamples;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>P14-02 criterion 2: the parser reads the documented shape (UNVERIFIED fixtures), turns strikes and prices into paise, turns a missing quote into null with a flag, flags bad Greeks and a crossed book, cuts the strikes to the window and never throws.</summary>
public sealed class OptionChainParserTests
{
    private static readonly Guid Underlying = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly DateOnly Expiry = new(2026, 10, 20);
    private static readonly DateTimeOffset Slot = At(12, 9, 20);
    private static readonly DateTimeOffset Rx = At(12, 9, 20, 4);

    private static ChainParseResult Parse(byte[] body, int each = 30) => OptionChainParser.ParseChain(body, Underlying, Expiry, Slot, Rx, each);

    [Fact]
    public void The_documented_answer_becomes_one_row_per_side_in_paise_with_the_slot_as_time_and_no_flags()
    {
        var result = Parse(Chain("25642.8", ("25650.000000", Leg(42528), Leg(42529, last: "132.5"))));

        result.Status.ShouldBe(ChainParseStatus.Ok);
        var ce = result.Rows.Single(r => r.Side == "CE");
        (ce.Ts, ce.UnderlyingId, ce.Expiry, ce.StrikePx, ce.RxAt, ce.SecurityId, ce.UnderlyingPx).ShouldBe((Slot, Underlying, Expiry, 2_565_000, Rx, 42528L, 2_564_280));
        (ce.LastPx, ce.AvgPx, ce.PrevClosePx, ce.BidPx, ce.BidQty, ce.AskPx, ce.AskQty).ShouldBe((13_400, 14_699, 24_485, 13_355, 1625, 13_400, 1365));
        (ce.Oi, ce.PrevOi, ce.Volume, ce.PrevVolume).ShouldBe((3_786_445L, 402_220L, 117_567_970L, 31_931_705L));
        (ce.Iv, ce.Delta, ce.Gamma, ce.Theta, ce.Vega).ShouldBe((9.789f, 0.53871f, 0.00132f, -15.1539f, 12.18593f));
        ce.Flags.ShouldBe((short)0);
        result.Rows.Single(r => r.Side == "PE").LastPx.ShouldBe(13_250);
    }

    [Theory]
    [InlineData("25650.000000", 2_565_000)]
    [InlineData("25650", 2_565_000)]
    [InlineData("100.125", 10_013)] // half away from zero
    [InlineData("0.005", 1)]
    public void A_strike_key_becomes_integer_paise_half_away_from_zero(string key, int paise)
    {
        OptionChainParser.StrikePaise(key).ShouldBe(paise);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1e3")]
    [InlineData("99999999999")]
    public void A_key_that_is_not_a_plain_positive_decimal_is_not_a_strike(string key)
    {
        OptionChainParser.StrikePaise(key).ShouldBeNull();
    }

    [Fact]
    public void A_zero_bid_ask_and_last_price_are_null_with_the_flags_and_the_quantity_goes_with_the_price()
    {
        var result = Parse(Chain("100", ("100", Leg(last: "0", bid: "0", ask: "0"), null)));

        var row = result.Rows.ShouldHaveSingleItem();
        (row.LastPx, row.BidPx, row.BidQty, row.AskPx, row.AskQty).ShouldBe(((int?)null, (int?)null, (int?)null, (int?)null, (int?)null));
        row.Flags.ShouldBe((short)(ChainFlags.NoBid | ChainFlags.NoAsk | ChainFlags.NoTrade));
    }

    [Fact]
    public void A_missing_bid_field_is_the_same_as_a_zero_one()
    {
        var leg = "{\"security_id\":7,\"last_price\":10,\"top_ask_price\":11,\"top_ask_quantity\":5}";

        var row = Parse(Chain("100", ("100", leg, null))).Rows.ShouldHaveSingleItem();

        (row.BidPx, row.AskPx, row.AskQty, row.LastPx).ShouldBe(((int?)null, (int?)1100, (int?)5, (int?)1000));
        row.Flags.ShouldBe(ChainFlags.NoBid);
        (row.Oi, row.Volume, row.Iv, row.Delta).ShouldBe(((long?)null, (long?)null, (float?)null, (float?)null), "a missing value is null, never zero");
    }

    [Theory]
    [InlineData("-1", "0.5", "0.1", "-3", "5")] // negative IV
    [InlineData("0", "0.5", "0.1", "-3", "5")] // a zero IV is no value
    [InlineData("9.7", "1.5", "0.1", "-3", "5")] // delta out of range
    [InlineData("9.7", "0.5", "-0.1", "-3", "5")] // negative gamma
    [InlineData("9.7", "0.5", "0.1", "-3", "-5")] // negative vega
    public void An_unusable_iv_or_greek_is_null_with_the_bad_greeks_flag_and_the_rest_of_the_row_is_kept(string iv, string delta, string gamma, string theta, string vega)
    {
        var row = Parse(Chain("100", ("100", Leg(iv: iv, delta: delta, gamma: gamma, theta: theta, vega: vega), null))).Rows.ShouldHaveSingleItem();

        (row.Flags & ChainFlags.BadGreeks).ShouldBe(ChainFlags.BadGreeks);
        row.LastPx.ShouldBe(13_400);
        var bad = new[] { (iv, row.Iv), (delta, row.Delta), (gamma, row.Gamma), (vega, row.Vega) };
        bad.Count(b => b.Item2 is null).ShouldBe(1);
    }

    [Fact]
    public void A_text_where_a_number_belongs_is_a_bad_greek_and_never_a_throw()
    {
        var row = Parse(Chain("100", ("100", Leg(iv: "\"NaN\"", theta: "\"Infinity\""), null))).Rows.ShouldHaveSingleItem();

        (row.Iv, row.Theta).ShouldBe(((float?)null, (float?)null));
        row.Flags.ShouldBe(ChainFlags.BadGreeks);
    }

    [Fact]
    public void A_negative_theta_and_a_negative_put_delta_are_normal()
    {
        var row = Parse(Chain("100", ("100", null, Leg(delta: "-0.47", theta: "-9.1")))).Rows.ShouldHaveSingleItem();

        (row.Delta, row.Theta, row.Flags).ShouldBe((-0.47f, -9.1f, (short)0));
    }

    [Fact]
    public void A_bid_above_the_ask_is_flagged_crossed_and_both_are_kept_as_sent()
    {
        var row = Parse(Chain("100", ("100", Leg(bid: "135", ask: "134"), null))).Rows.ShouldHaveSingleItem();

        (row.BidPx, row.AskPx, row.Flags).ShouldBe((13_500, 13_400, ChainFlags.Crossed));
    }

    [Fact]
    public void Only_the_strikes_within_the_window_of_the_one_nearest_the_underlying_are_kept()
    {
        var result = Parse(Ladder("25000", 21), each: 3); // strikes 25000..26000 step 50, underlying exactly on the first

        result.Rows.Select(r => r.StrikePx).Distinct().Order().ShouldBe([2_500_000, 2_505_000, 2_510_000, 2_515_000]);

        var middle = Parse(Ladder("25520", 21), each: 2); // nearest strike is 25500 (index 10)
        middle.Rows.Select(r => r.StrikePx).Distinct().Order().ShouldBe([2_540_000, 2_545_000, 2_550_000, 2_555_000, 2_560_000]);
    }

    [Fact]
    public void A_side_that_is_missing_has_no_row_and_a_leg_without_a_security_id_is_skipped()
    {
        var noId = "{\"last_price\":10}";

        var result = Parse(Chain("100", ("100", Leg(), null), ("110", noId, Leg(55))));

        result.Rows.Select(r => (r.StrikePx, r.Side)).ShouldBe([(10_000, "CE"), (11_000, "PE")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"oc\":{}}}")]
    [InlineData("{\"data\":{\"last_price\":0,\"oc\":{\"100\":{}}}}")]
    [InlineData("{\"data\":{\"last_price\":\"x\",\"oc\":{}}}")]
    [InlineData("{\"data\":{\"last_price\":100,\"oc\":[]}}")]
    [InlineData("{\"data\":{\"last_price\":100,\"oc\":{\"abc\":{}}}}")]
    public void A_body_that_is_not_the_documented_shape_is_one_bad_body_and_never_a_throw(string body)
    {
        var result = Parse(Encoding.UTF8.GetBytes(body));

        result.Status.ShouldBe(ChainParseStatus.BadBody);
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void A_chain_without_a_strike_is_no_data()
    {
        Parse(Encoding.UTF8.GetBytes("{\"data\":{\"last_price\":100,\"oc\":{}}}")).Status.ShouldBe(ChainParseStatus.NoData);
    }

    [Fact]
    public void The_expiry_list_is_read_ascending_and_distinct_and_a_wrong_shape_is_null()
    {
        OptionChainParser.ParseExpiries(Expiries("2026-11-10", "2026-11-03", "2026-11-03")).ShouldBe([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 10)]);
        OptionChainParser.ParseExpiries(Encoding.UTF8.GetBytes("{\"data\":[\"2026-13-40\"]}")).ShouldBeNull();
        OptionChainParser.ParseExpiries(Encoding.UTF8.GetBytes("{\"data\":{}}")).ShouldBeNull();
        OptionChainParser.ParseExpiries(Encoding.UTF8.GetBytes("nope")).ShouldBeNull();
        OptionChainParser.ParseExpiries(Encoding.UTF8.GetBytes("{\"data\":[]}")).ShouldBeEmpty();
    }
}

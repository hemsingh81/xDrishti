using XDrishti.Domain.Research;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File.Tests.Support;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File.Tests;

/// <summary>Criteria 3 and 8 of P3-02c: the canonical text and the hash are taken from the model, so syntax never reaches them; the built-ins keep the hashes the run records already carry.</summary>
public sealed class CanonicalFormTests
{
    private static string HashOf(string text) => StrategyHash.Of(StrategyFile.Read(text).Definition!);

    [Fact]
    public void White_space_key_order_comments_quoting_number_spelling_style_and_version_do_not_change_the_hash()
    {
        var plain = HashOf(Example.Text);

        StrategyFile.Read(Example.Perturbed).HasErrors.ShouldBeFalse();
        CanonicalStrategy.Json(StrategyFile.Read(Example.Perturbed).Definition!).ShouldBe(CanonicalStrategy.Json(StrategyFile.Read(Example.Text).Definition!));
        HashOf(Example.Perturbed).ShouldBe(plain);
        HashOf(Example.Text.Replace("version: 1", "version: 7")).ShouldBe(plain);
        HashOf(Example.Text.Replace("\n", "\r\n")).ShouldBe(plain);
        HashOf(Example.Text[..Example.Text.IndexOf("\ntest:", StringComparison.Ordinal)]).ShouldBe(plain, "universe and test are outside the hash");
        HashOf(Example.Text.Replace("rr: [2.0]", "rr: [2.00]")).ShouldBe(plain);
        plain.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void The_order_of_params_does_not_change_the_canonical_text_but_the_order_of_conditions_does()
    {
        var definition = Example.Model();
        var reversed = definition with { Params = [.. definition.Params.Reverse()] };
        var swapped = definition with { Buy = definition.Buy! with { Setup = new ConditionList(ListMode.All, [.. definition.Buy.Setup!.Items.Reverse()]) } };

        CanonicalStrategy.Json(reversed).ShouldBe(CanonicalStrategy.Json(definition));
        CanonicalStrategy.Json(swapped).ShouldNotBe(CanonicalStrategy.Json(definition));
        CanonicalStrategy.Json(definition).ShouldStartWith("{\"schema\":1,\"id\":\"VWAP_RSI_TREND\"");
        CanonicalStrategy.Json(definition).ShouldContain("\"params\":[{\"name\":\"rsi_len\"");
    }

    [Theory]
    [InlineData("rsi(rsi_len) > rsi_min", "rsi(rsi_len) >= rsi_min")]
    [InlineData("rsi_min: 55", "rsi_min: 56")]
    [InlineData("rr: [2.0]", "rr: [2.5]")]
    [InlineData("id: VWAP_RSI_TREND", "id: VWAP_RSI_TREND2")]
    [InlineData("style: intraday", "style: swing")]
    [InlineData("timeframes: [15m]", "timeframes: [30m]")]
    [InlineData("sides: [BUY, SELL]", "sides: [BUY]")]
    [InlineData("type: at_close", "type: stop_above_high")]
    [InlineData("type: swing_low", "type: swing_high")]
    [InlineData("buffer_atr: 0.1", "buffer_atr: 0.2")]
    [InlineData("{ policy: FIXED_RR, rr: [2.0] }", "{ policy: FIXED_RR, rr: [2.0, 3.0] }")]
    [InlineData("book_pct: 50", "book_pct: 51")]
    [InlineData("last_entry: \"14:30\"", "last_entry: \"14:31\"")]
    [InlineData("filters: [not_event_day, min_turnover]", "filters: [not_event_day]")]
    [InlineData("all:\n      - crosses_above(close, vwap)", "any:\n      - crosses_above(close, vwap)")]
    [InlineData("rsi_len: 14", "rsi_len: 15")]
    [InlineData("t1_rr: 1.0", "t1_rr: 1.5")]
    [InlineData("lookback: swing_lookback", "lookback: 5")]
    [InlineData("rsi(rsi_len)", "rsi()")]
    [InlineData("vwap)", "vwap) and close > 0")]
    public void One_changed_token_changes_the_hash(string before, string after)
    {
        var changed = Example.Text.Replace(before, after);

        changed.ShouldNotBe(Example.Text);
        HashOf(changed).ShouldNotBe(HashOf(Example.Text));
    }

    [Theory]
    [InlineData(OrbV1.Id, "9c1141cf1835d03a0b25b280bbab9b2a7367db8e13ec7568cb0083ca9db14e03")]
    [InlineData(VwapPullback.Id, "7c1c2493b220e3c343c50960b08a623d48be346e198c7bc233710aaccbe7cca9")]
    [InlineData(BaseBreakoutV1.Id, "c41e6a82171fbef80953512750c2eec2a300c461893d6ff627a936956b585374")]
    public void The_hash_of_a_built_in_is_the_one_every_stored_run_already_names(string id, string hash)
    {
        var definition = id switch { OrbV1.Id => OrbV1.Create(), VwapPullback.Id => VwapPullback.Create(), _ => BaseBreakoutV1.Create() };

        StrategyHash.Of(definition).ShouldBe(hash);
        CanonicalText.Sha256Hex(CanonicalStrategy.Json(definition)).ShouldBe(hash);
        StrategyHash.Of(StrategyFile.Read(StrategyFile.Write(definition)).Definition!).ShouldBe(hash);
    }

    [Fact]
    public void The_canonical_text_is_the_same_after_a_file_round_trip_for_every_definition_of_the_corpus()
    {
        foreach (var definition in Corpus.Definitions(seed: 21, count: 200))
        {
            CanonicalStrategy.Json(StrategyFile.Read(StrategyFile.Write(definition)).Definition!).ShouldBe(CanonicalStrategy.Json(definition));
        }
    }

    [Fact]
    public void Unsorted_params_and_exit_parameters_hash_like_the_sorted_ones()
    {
        var definition = Example.Model();
        var exit = definition.Buy!.Exits![1];
        var shuffled = definition with
        {
            Params = [.. definition.Params.Reverse()],
            Buy = definition.Buy with { Exits = [definition.Buy.Exits[0], exit with { Parameters = [.. exit.Parameters.Reverse()] }] },
        };

        StrategyHash.Of(shuffled).ShouldBe(StrategyHash.Of(definition));
    }

    [Theory]
    [InlineData("a: 1\nb: [")]
    [InlineData("strategy: !!binary x\n")]
    [InlineData("\"\\uD800\"")]
    [InlineData("- &a [*a]")]
    [InlineData("? |\n  x\n: y")]
    public void Odd_input_is_a_problem_never_an_exception(string text)
    {
        var result = StrategyFile.Read(text);

        result.Definition.ShouldBeNull();
        result.HasErrors.ShouldBeTrue();
    }
}

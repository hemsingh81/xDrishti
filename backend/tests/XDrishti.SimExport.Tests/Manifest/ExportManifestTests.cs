using System.Text;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Manifest;

/// <summary>Criterion 8 of P3-04b1: the manifest, the export key and the run id. The known answers of the key were computed with an independent tool (Python 3 <c>hashlib</c>) on 2026-10-18 from the lines the spec gives.</summary>
public sealed class ExportManifestTests
{
    private const string Format = "xd-sim-export-1";

    private const string RunKey = "3fa91c2d07be0000000000000000000000000000000000000000000000000000";

    private const string KeyOfFour = "a6288e82335599791431edc24b2a9b9cc339ba508ac867ac960bbb794f836ea4";

    private const string KeyOfFiveWithExtras = "2e27b1ed0c8776a53f8516c0c88420ecafa8cbc065e542fb1454d1fd17307bd2";

    private static readonly Guid Basket = new("11111111-2222-3333-4444-555555555555");

    private static readonly ManifestEntry Trades = new("trades.csv", 120, "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb");

    private static readonly ManifestEntry Readme = new("README.txt", 30, "3e23e8160039594a33894f6564e1b1348bbd7a0088d42c4acb73eeaed59c009d");

    private static readonly ManifestEntry Json = new("run.json", 900, "2e7d2c03a9507ae265ecf5b5356885a53393a2029d241394997265a1a25aefc6");

    private static readonly ManifestEntry Open = new("open_at_end.csv", 40, "18ac3e7343f016890c510e93f935261169d9e3f565436429830faf0934f4f8e4");

    private static readonly ManifestEntry Card = new("cards/0001-x.html", 7, "3f79bb7b435b05321651daefd374cdc681dc06faa65e374e38337b88ca046dea");

    private static readonly ManifestEntry[] Four = [Trades, Readme, Json, Open];

    private static readonly string[] Extras = ["cards.cap=200", "cards.selected=ORB|NSE:AAA|Buy|2026-03-02|first"];

    [Fact]
    public void The_key_equals_the_known_answer_of_an_independent_tool()
    {
        ExportKey.Of(Format, RunKey, null, Four, []).ShouldBe(KeyOfFour);
        ExportKey.Of("xd-sim-export-2", RunKey, Basket, [.. Four, Card], Extras).ShouldBe(KeyOfFiveWithExtras);
    }

    [Fact]
    public void The_key_changes_when_any_one_input_changes()
    {
        var baseKey = ExportKey.Of(Format, RunKey, Basket, Four, Extras);

        new[]
        {
            ExportKey.Of("xd-sim-export-2", RunKey, Basket, Four, Extras),
            ExportKey.Of(Format, "4fa91c2d07be" + RunKey[12..], Basket, Four, Extras),
            ExportKey.Of(Format, RunKey, null, Four, Extras),
            ExportKey.Of(Format, RunKey, new Guid("11111111-2222-3333-4444-555555555556"), Four, Extras),
            ExportKey.Of(Format, RunKey, Basket, [Trades with { Sha256 = new string('0', 64) }, Readme, Json, Open], Extras),
            ExportKey.Of(Format, RunKey, Basket, Four, [Extras[0], Extras[1] + "x"]),
            ExportKey.Of(Format, RunKey, Basket, Four, [Extras[1], Extras[0]]),
            ExportKey.Of(Format, RunKey, Basket, Four, [Extras[0]]),
            ExportKey.Of(Format, RunKey, Basket, [Trades, Readme, Json], Extras),
        }.Concat([baseKey]).Distinct().Count().ShouldBe(10, "each change gives another key");
    }

    [Fact]
    public void The_key_does_not_depend_on_the_order_the_entries_are_given_in_nor_on_the_length()
    {
        var key = ExportKey.Of(Format, RunKey, Basket, Four, Extras);

        ExportKey.Of(Format, RunKey, Basket, [Open, Json, Readme, Trades], Extras).ShouldBe(key);
        ExportKey.Of(Format, RunKey, Basket, [Trades with { Bytes = 1 }, Readme, Json, Open], Extras).ShouldBe(key, "the hash covers the content");
    }

    [Fact]
    public void The_build_equals_the_golden_file_golden()
    {
        var manifest = ExportManifest.Build("xd-sim-export-2", RunKey, Basket, [.. Four, Card], Extras);

        Golden.Check("manifest", Encoding.UTF8.GetString(manifest));
    }

    [Fact]
    public void The_manifest_lists_the_files_in_ordinal_name_order_compact_with_no_time()
    {
        var text = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, []));

        text.ShouldBe(
            "{\"format\":\"xd-sim-export-1\",\"run_key\":\"" + RunKey + "\",\"basket_id\":null,\"export_key\":\"" + KeyOfFour + "\",\"files\":["
            + "{\"name\":\"README.txt\",\"bytes\":30,\"sha256\":\"" + Readme.Sha256 + "\"},"
            + "{\"name\":\"open_at_end.csv\",\"bytes\":40,\"sha256\":\"" + Open.Sha256 + "\"},"
            + "{\"name\":\"run.json\",\"bytes\":900,\"sha256\":\"" + Json.Sha256 + "\"},"
            + "{\"name\":\"trades.csv\",\"bytes\":120,\"sha256\":\"" + Trades.Sha256 + "\"}],\"extra\":[]}");
    }

    [Fact]
    public void Parse_gives_back_the_format_the_run_key_the_basket_the_key_the_entries_and_the_extra_lines()
    {
        var bytes = ExportManifest.Build("xd-sim-export-2", RunKey, Basket, [.. Four, Card], Extras);

        var parsed = ExportManifest.Parse(bytes);

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.Format.ShouldBe("xd-sim-export-2");
        parsed.Value.RunKey.ShouldBe(RunKey);
        parsed.Value.BasketId.ShouldBe(Basket);
        parsed.Value.ExportKey.ShouldBe(KeyOfFiveWithExtras);
        parsed.Value.Entries.ShouldBe([Readme, Card, Open, Json, Trades]);
        parsed.Value.ExtraLines.ShouldBe(Extras);
    }

    [Fact]
    public void A_manifest_without_a_basket_parses_with_no_basket()
    {
        ExportManifest.Parse(ExportManifest.Build(Format, RunKey, null, Four, [])).Value.BasketId.ShouldBeNull();
    }

    [Fact]
    public void Adding_a_hand_made_entry_in_a_subfolder_and_two_extra_lines_needs_no_change_to_any_type()
    {
        // what P3-04c does: the five files of b2, an index and the cards, extra lines for the cap and the chosen trades
        var cards = new ManifestEntry("cards/0001-x.html", 7, new string('e', 64));
        var index = new ManifestEntry("index.html", 11, new string('f', 64));

        var bytes = ExportManifest.Build("xd-sim-export-2", RunKey, null, [.. Four, index, cards], ["cards.cap=200", "cards.selected=k|first"]);
        var parsed = ExportManifest.Parse(bytes).Value;

        parsed.Entries.Select(e => e.Name).ShouldBe(["README.txt", "cards/0001-x.html", "index.html", "open_at_end.csv", "run.json", "trades.csv"]);
        parsed.ExtraLines.ShouldBe(["cards.cap=200", "cards.selected=k|first"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"x\"}")]
    public void A_text_that_is_not_a_manifest_is_malformed(string text)
    {
        var parsed = ExportManifest.Parse(Encoding.UTF8.GetBytes(text));

        parsed.IsFailure.ShouldBeTrue();
        parsed.Error.Code.ShouldBe("manifest.malformed");
    }

    [Fact]
    public void A_manifest_with_a_missing_member_an_extra_member_or_a_wrong_kind_is_malformed()
    {
        var good = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, []));

        foreach (var broken in new[]
        {
            good.Replace("\"extra\":[]", "\"extras\":[]", StringComparison.Ordinal),
            good.Replace("\"basket_id\":null,", string.Empty, StringComparison.Ordinal),
            good.Replace("}", ",\"more\":1}", StringComparison.Ordinal),
            good.Replace("\"bytes\":30", "\"bytes\":\"30\"", StringComparison.Ordinal),
            good.Replace("\"bytes\":30", "\"bytes\":-1", StringComparison.Ordinal),
            good.Replace("\"basket_id\":null", "\"basket_id\":7", StringComparison.Ordinal),
            good.Replace("\"basket_id\":null", "\"basket_id\":\"not-a-guid\"", StringComparison.Ordinal),
            good.Replace(Readme.Sha256, "ABC", StringComparison.Ordinal),
            good.Replace(RunKey, "short", StringComparison.Ordinal),
        })
        {
            ExportManifest.Parse(Encoding.UTF8.GetBytes(broken)).Error.Code.ShouldBe("manifest.malformed", broken);
        }
    }

    [Fact]
    public void A_manifest_that_was_edited_after_it_was_written_does_not_match_its_own_key()
    {
        var good = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, []));
        var edited = good.Replace(Json.Sha256, new string('9', 64), StringComparison.Ordinal);

        ExportManifest.Parse(Encoding.UTF8.GetBytes(edited)).Error.Code.ShouldBe("manifest.malformed");
    }

    [Fact]
    public void A_hand_edited_manifest_with_a_line_break_or_a_lone_surrogate_in_a_string_is_malformed_and_never_throws()
    {
        var good = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, ["x"]));

        foreach (var broken in new[]
        {
            good.Replace("xd-sim-export-1", "a\\nb", StringComparison.Ordinal),
            good.Replace("xd-sim-export-1", "a\\ud800", StringComparison.Ordinal),
            good.Replace("\"x\"", "\"a\\ud800\"", StringComparison.Ordinal),
            good.Replace("\"x\"", "\"a\\nb\"", StringComparison.Ordinal),
            good.Replace("trades.csv", "a\\ud800", StringComparison.Ordinal),
            good.Replace(RunKey, "\\ud800", StringComparison.Ordinal),
        })
        {
            ExportManifest.Parse(Encoding.UTF8.GetBytes(broken)).Error.Code.ShouldBeOneOf("manifest.malformed", "manifest.names_invalid");
        }
    }

    [Theory]
    [InlineData("../evil.csv")]
    [InlineData("a/../b")]
    [InlineData("a\\\\b.csv")]
    [InlineData("a/b/c.csv")]
    [InlineData("/abs.csv")]
    [InlineData("trailing/")]
    [InlineData("with space.csv")]
    [InlineData("colon:name")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public void A_name_that_is_not_valid_gives_the_names_code_when_parsed(string name)
    {
        var good = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, []));
        var edited = good.Replace("\"name\":\"trades.csv\"", "\"name\":\"" + name + "\"", StringComparison.Ordinal);

        ExportManifest.Parse(Encoding.UTF8.GetBytes(edited)).Error.Code.ShouldBe("manifest.names_invalid");
    }

    [Fact]
    public void A_duplicate_name_or_two_names_that_differ_only_in_case_give_the_names_code()
    {
        var good = Encoding.UTF8.GetString(ExportManifest.Build(Format, RunKey, null, Four, []));

        foreach (var name in new[] { "run.json", "RUN.JSON" })
        {
            var edited = good.Replace("\"name\":\"trades.csv\"", "\"name\":\"" + name + "\"", StringComparison.Ordinal);

            ExportManifest.Parse(Encoding.UTF8.GetBytes(edited)).Error.Code.ShouldBe("manifest.names_invalid", name);
        }
    }

    [Fact]
    public void Build_and_key_refuse_what_the_parse_would_refuse()
    {
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, [Trades with { Name = "../x" }], []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, [Trades, Trades], []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, [Trades, Trades with { Name = "TRADES.CSV" }], []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, [Trades with { Sha256 = "xyz" }], []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, [Trades with { Bytes = -1 }], []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, "abc", null, Four, []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(string.Empty, RunKey, null, Four, []));
        Should.Throw<ArgumentException>(() => ExportKey.Of("a\nb", RunKey, null, Four, []));
        Should.Throw<ArgumentException>(() => ExportKey.Of(Format, RunKey, null, Four, ["line\nbreak"]));
        Should.Throw<ArgumentException>(() => ExportManifest.Build(Format, RunKey, null, [Trades with { Name = "a/b/c" }], []));
    }

    [Fact]
    public void An_empty_list_of_files_is_a_valid_manifest()
    {
        ExportManifest.Parse(ExportManifest.Build(Format, RunKey, null, [], [])).Value.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("VWAP_PULLBACK", "vwap-pullback-20240401-20260430-3fa91c2d07be")]
    [InlineData("BASE_BREAKOUT", "base-breakout-20240401-20260430-3fa91c2d07be")]
    [InlineData("ORB", "orb-20240401-20260430-3fa91c2d07be")]
    [InlineData("Orb2", "orb2-20240401-20260430-3fa91c2d07be")]
    public void The_run_id_is_the_strategy_the_window_and_the_start_of_the_run_key(string strategy, string expected)
    {
        RunId.Of(strategy, new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey).ShouldBe(expected);
    }

    [Theory]
    [InlineData("_x")]
    [InlineData("_")]
    public void A_strategy_id_that_starts_with_an_underscore_is_refused_because_the_run_id_would_start_with_a_hyphen(string strategy)
    {
        Should.Throw<ArgumentException>(() => RunId.Of(strategy, new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("é")]
    public void A_strategy_id_with_a_character_outside_the_set_is_refused(string strategy)
    {
        Should.Throw<ArgumentException>(() => RunId.Of(strategy, new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey));
    }

    [Fact]
    public void The_run_id_is_at_most_80_characters_of_lower_case_letters_digits_and_hyphens()
    {
        var longest = new string('A', 49);

        var id = RunId.Of(longest, new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey);

        id.Length.ShouldBe(80);
        id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-').ShouldBeTrue();
        Should.Throw<ArgumentException>(() => RunId.Of(new string('A', 50), new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey));
        Should.Throw<ArgumentException>(() => RunId.Of("ORB", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), "abc"));
    }

    [Fact]
    public void A_run_id_is_a_valid_folder_name_for_the_names_rules()
    {
        ExportNames.IsValid(RunId.Of("VWAP_PULLBACK", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30), RunKey)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("trades.csv", true)]
    [InlineData("cards/0001-NSE_M&M-buy.html", true)]
    [InlineData("a.b-c_d&e", true)]
    [InlineData("a/b/c", false)]
    [InlineData("..", false)]
    [InlineData("a/..", false)]
    [InlineData("x\0y", false)]
    [InlineData("x\ty", false)]
    [InlineData("x\u202Ey", false)]
    [InlineData("é", false)]
    public void The_name_rules_are_the_ones_of_the_spec(string name, bool valid)
    {
        ExportNames.IsValid(name).ShouldBe(valid);
    }

    [Fact]
    public void A_name_or_a_segment_that_is_too_long_is_not_valid()
    {
        ExportNames.IsValid(new string('a', 100)).ShouldBeTrue();
        ExportNames.IsValid(new string('a', 101)).ShouldBeFalse();
        ExportNames.IsValid("cards/" + new string('a', 101)).ShouldBeFalse();
        ExportNames.IsValid(new string('a', 100) + "/" + new string('b', 100) + "x").ShouldBeFalse();
    }
}

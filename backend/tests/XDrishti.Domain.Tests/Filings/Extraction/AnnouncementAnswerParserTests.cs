using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Domain.Tests.Filings.Extraction;

/// <summary>P15-08 criterion 2: the strict parser. A valid answer parses; every other shape is invalid with a closed reason and nothing of it is kept.</summary>
public sealed class AnnouncementAnswerParserTests
{
    private const string Valid = """{"material":true,"category":"order_win","reason":"A large order was received.","order_value_inr":45000000,"guidance":"none","rating_action":"none"}""";

    private static string With(string key, string value) => System.Text.RegularExpressions.Regex.Replace(Valid, "\"" + key + "\":(\"[^\"]*\"|[^,}]+)", "\"" + key + "\":" + value);

    [Fact]
    public void A_valid_answer_parses_into_its_six_fields()
    {
        var parsed = AnnouncementAnswerParser.Parse(Valid);

        parsed.IsValid.ShouldBeTrue();
        parsed.Reason.ShouldBeNull();
        parsed.Answer.ShouldBe(new AnnouncementAnswer(true, "order_win", "A large order was received.", 45_000_000m, "none", "none"));
    }

    [Theory]
    [InlineData("  \n{\"material\":false,\"category\":\"procedural\",\"reason\":\"Routine.\",\"order_value_inr\":null,\"guidance\":\"maintained\",\"rating_action\":\"reaffirmed\"}\n ")]
    [InlineData("{\"rating_action\":\"withdrawn\",\"guidance\":\"lowered\",\"order_value_inr\":12.5,\"reason\":\"नियमित reason\",\"category\":\"other\",\"material\":false}")]
    public void Whitespace_around_the_object_a_different_key_order_a_decimal_value_and_unicode_text_are_valid(string text) => AnnouncementAnswerParser.Parse(text).IsValid.ShouldBeTrue();

    [Fact]
    public void A_reason_of_exactly_200_characters_is_valid_and_201_is_not()
    {
        AnnouncementAnswerParser.Parse(With("reason", "\"" + new string('r', 200) + "\"")).IsValid.ShouldBeTrue();
        AnnouncementAnswerParser.Parse(With("reason", "\"" + new string('r', 201) + "\"")).Reason.ShouldBe("reason_too_long");
    }

    [Theory]
    [InlineData("""{"material":true,"category":"order_win","reason":"x","order_value_inr":1,"guidance":"none","rating_action":"none","tool":"run_sql"}""", "bad_keys")]
    [InlineData("""{"material":true,"category":"order_win","reason":"x","order_value_inr":1,"guidance":"none"}""", "bad_keys")]
    [InlineData("""{"material":true,"material":false,"category":"order_win","reason":"x","order_value_inr":1,"guidance":"none","rating_action":"none"}""", "bad_keys")]
    [InlineData("""{"name":"run_sql","arguments":{"sql":"select 1"}}""", "bad_keys")]
    [InlineData("""{"Material":true,"category":"order_win","reason":"x","order_value_inr":1,"guidance":"none","rating_action":"none"}""", "bad_keys")]
    [InlineData("""{}""", "bad_keys")]
    public void A_wrong_set_of_keys_is_invalid(string text, string reason) => AnnouncementAnswerParser.Parse(text).ShouldBe(AnswerParse.Invalid(reason));

    [Theory]
    [InlineData("category", "\"Results\"")]
    [InlineData("category", "\"results \"")]
    [InlineData("category", "\"ORDER_WIN\"")]
    [InlineData("category", "\"anything\"")]
    [InlineData("category", "null")]
    [InlineData("guidance", "\"up\"")]
    [InlineData("rating_action", "\"none \"")]
    [InlineData("rating_action", "3")]
    [InlineData("material", "\"true\"")]
    [InlineData("material", "1")]
    [InlineData("material", "null")]
    [InlineData("reason", "\"\"")]
    [InlineData("reason", "\"   \"")]
    [InlineData("reason", "\"line one\\nline two\"")]
    [InlineData("reason", "\"right to left \\u202e override\"")]
    [InlineData("reason", "\"zero\\u200bwidth\"")]
    [InlineData("reason", "\"mark\\u200ehere\"")]
    [InlineData("reason", "\"mark\\u200fhere\"")]
    [InlineData("reason", "\"line\\u2028sep\"")]
    [InlineData("reason", "\"para\\u2029sep\"")]
    [InlineData("reason", "{\"nested\":true}")]
    [InlineData("reason", "12")]
    [InlineData("order_value_inr", "\"45000000\"")]
    [InlineData("order_value_inr", "-1")]
    [InlineData("order_value_inr", "1e30")]
    [InlineData("order_value_inr", "true")]
    [InlineData("order_value_inr", "[1]")]
    public void A_wrong_value_is_invalid_with_bad_value(string key, string value) => AnnouncementAnswerParser.Parse(With(key, value)).ShouldBe(AnswerParse.Invalid("bad_value"));

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   \n", "empty")]
    [InlineData("I think this is material.", "not_json")]
    [InlineData("Sure! Here is the answer: {\"material\":true}", "not_json")]
    [InlineData("```json\n{\"material\":true}\n```", "not_json")]
    [InlineData("[{\"material\":true}]", "not_one_object")]
    [InlineData("\"material\"", "not_one_object")]
    [InlineData("true", "not_one_object")]
    public void Prose_fences_arrays_and_empty_text_are_not_an_answer(string text, string reason) => AnnouncementAnswerParser.Parse(text).ShouldBe(AnswerParse.Invalid(reason));

    [Fact]
    public void Two_objects_prose_after_the_object_and_a_cut_object_are_not_one_object()
    {
        AnnouncementAnswerParser.Parse(Valid + Valid).Reason.ShouldBe("not_one_object");
        AnnouncementAnswerParser.Parse(Valid + "\nHope that helps!").Reason.ShouldBe("not_one_object");
        AnnouncementAnswerParser.Parse(Valid + ",").Reason.ShouldBe("not_one_object");
        AnnouncementAnswerParser.Parse(Valid[..^10]).Reason.ShouldBe("not_one_object");
        AnnouncementAnswerParser.Parse("{\"material\":true,}").Reason.ShouldBe("not_one_object");
    }

    [Fact]
    public void A_replacement_character_and_a_lone_surrogate_are_bad_encoding()
    {
        AnnouncementAnswerParser.Parse(With("reason", "\"bad \uFFFD char\"")).ShouldBe(AnswerParse.Invalid("bad_encoding"));
        AnnouncementAnswerParser.Parse(With("reason", "\"lone \uD800 surrogate\"")).ShouldBe(AnswerParse.Invalid("bad_encoding"));
    }

    [Fact]
    public void Every_invalid_result_carries_no_answer_and_a_closed_reason_of_at_most_30_characters()
    {
        string[] bad = ["", "x", "{}", Valid + Valid, With("category", "\"x\""), With("reason", "\"" + new string('r', 300) + "\"")];

        foreach (var text in bad)
        {
            var parsed = AnnouncementAnswerParser.Parse(text);

            parsed.Answer.ShouldBeNull();
            parsed.Reason.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(30);
            parsed.Reason.ShouldNotContain("r".PadLeft(5, 'r'));
        }
    }

    [Fact]
    public void The_schema_lists_the_closed_categories_and_enums_of_the_spec()
    {
        AnnouncementSchema.Categories.ShouldBe(["results", "order_win", "acquisition_or_merger", "fund_raising", "credit_rating", "guidance", "management_change", "litigation_or_regulatory", "default_or_delay", "dividend_or_buyback", "procedural", "other"]);
        AnnouncementSchema.GuidanceValues.ShouldBe(["raised", "lowered", "maintained", "none"]);
        AnnouncementSchema.RatingActions.ShouldBe(["upgrade", "downgrade", "reaffirmed", "withdrawn", "none"]);
        AnnouncementSchema.Keys.ShouldBe(["material", "category", "reason", "order_value_inr", "guidance", "rating_action"]);
        AnnouncementSchema.MaxReasonLength.ShouldBe(200);
    }
}

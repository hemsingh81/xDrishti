using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Domain.Tests.Assistant.Drafting;

/// <summary>Spec P9-07 AC 4: the model's answer is data; a field outside the schema, an unbounded list or a control character does not get through.</summary>
public sealed class DraftAnswerReaderTests
{
    private const string Good = """{"decline":false,"unsupported":[],"understood":["a"],"assumptions":[{"field":"rr","value":"2","why":"r"}],"not_expressed":[],"requested_test":{"from":"2022","to":null,"baskets":["B"]},"yaml":"id: X\n"}""";

    [Fact]
    public void A_good_answer_is_read()
    {
        var read = DraftAnswerReader.Read(Good);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Assumptions.Single().Field.ShouldBe("rr");
        read.Value.RequestedTest.Baskets.ShouldBe(["B"]);
        read.Value.Yaml.ShouldBe("id: X\n");
    }

    [Fact]
    public void A_fenced_answer_is_read() => DraftAnswerReader.Read("```json\n" + Good + "\n```").IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"decline":false,"extra":1}""")]
    [InlineData("""{"understood":[1]}""")]
    [InlineData("""{"yaml":5}""")]
    [InlineData("""{"assumptions":[{"field":"a","value":"b","why":"c","more":"d"}]}""")]
    [InlineData("""{"assumptions":[{"field":"a"}]}""")]
    [InlineData("""{"requested_test":{"bogus":1}}""")]
    public void A_shape_outside_the_schema_is_refused(string text) => DraftAnswerReader.Read(text).Error.Code.ShouldBe(DraftCodes.AnswerShape);

    [Fact]
    public void Lists_are_bounded()
    {
        var nine = string.Join(',', Enumerable.Repeat("\"x\"", 9));

        DraftAnswerReader.Read("{\"understood\":[" + nine + "]}").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Text_is_cleaned_of_control_characters_and_cut()
    {
        var read = DraftAnswerReader.Read("{\"understood\":[\"a\\u0007b  c\\n" + new string('z', 600) + "\"]}");

        read.Value.Understood.Single().ShouldStartWith("a b c ");
        read.Value.Understood.Single().Length.ShouldBe(DraftAnswerReader.MaxText);
    }

    [Fact]
    public void A_text_over_64_KB_is_refused() =>
        DraftAnswerReader.Read("{\"yaml\":\"" + new string('a', 70_000) + "\"}").IsFailure.ShouldBeTrue();
}

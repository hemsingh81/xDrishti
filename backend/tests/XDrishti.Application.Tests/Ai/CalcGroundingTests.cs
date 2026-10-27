using XDrishti.Application.Ai;

namespace XDrishti.Application.Tests.Ai;

/// <summary>ADR 0139: calc cannot launder an invented figure; its result is a source only when each operand came from another tool's result of the turn.</summary>
public sealed class CalcGroundingTests
{
    private const string Report = """{"ok":true,"tool":"get_report","rows":[{"a":12.5,"b":4}]}""";

    private static string Calc(string result, params string[] operands) =>
        "{\"ok\":true,\"tool\":\"calc\",\"data\":{\"result\":\"" + result + "\",\"places\":4,\"operands\":[" + string.Join(",", operands.Select(o => "\"" + o + "\"")) + "]}}";

    [Fact]
    public void A_derivation_of_two_earlier_results_is_grounded()
    {
        CalcGrounding.IsGrounded(Calc("3.125", "12.5", "4"), [Report], "how much?").ShouldBeTrue();
    }

    [Fact]
    public void An_invented_operand_is_not_grounded()
    {
        CalcGrounding.IsGrounded(Calc("0.9", "0.9", "0"), [Report], "how much?").ShouldBeFalse();
    }

    [Fact]
    public void The_structural_literals_need_no_source_but_other_numbers_do()
    {
        CalcGrounding.IsGrounded(Calc("1250", "12.5", "100"), [Report], "q").ShouldBeTrue();
        CalcGrounding.IsGrounded(Calc("25", "12.5", "2"), [Report], "q").ShouldBeFalse();
    }

    [Fact]
    public void An_operand_from_the_owners_question_is_allowed()
    {
        CalcGrounding.IsGrounded(Calc("25", "12.5", "2"), [Report], "double 2 please").ShouldBeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"ok":false,"error":{"code":"calc.rejected"}}""")]
    [InlineData("""{"ok":true,"data":{"result":"1"}}""")]
    public void A_result_without_operands_or_a_failed_one_is_never_a_source(string text)
    {
        CalcGrounding.IsGrounded(text, [Report], "q").ShouldBeFalse();
    }
}

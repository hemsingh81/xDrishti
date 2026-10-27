using System.Text.Json.Nodes;
using XDrishti.Application.Ai;

namespace XDrishti.Application.Tests.Assistant;

/// <summary>Spec P9-05 AC 12: the chart takes columns of earlier results of the turn and no number from the model; a bad reference is named.</summary>
public sealed class ChartToolTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 5, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<int, string> Results = new()
    {
        [1] = """{"ok":true,"tool":"get_report","rows":[{"basket":"A","expectancyR":{"state":"value","number":0.8},"n":40,"note":"x"},{"basket":"B","expectancyR":{"state":"insufficient"},"n":3,"note":"y"}],"error":null}""",
        [2] = """{"ok":false,"tool":"get_report","rows":null,"error":{"code":"reports.invalid","message":"m"}}""",
        [3] = """{"ok":true,"tool":"list_runs","rows":[{"k":"a"}],"error":null}""",
    };

    private static string Args(string y = """[{"callIndex":1,"column":"n","label":"Trades"}]""", string kind = "bar", string title = "T", string x = """{"callIndex":1,"column":"basket"}""") =>
        $$"""{"kind":"{{kind}}","title":"{{title}}","x":{{x}},"y":{{y}}}""";

    [Fact]
    public void A_reference_to_columns_of_an_earlier_result_gives_a_spec_that_holds_no_number()
    {
        var json = ChartTool.Run(Args("""[{"callIndex":1,"column":"expectancyR","label":"Expectancy (R)"},{"callIndex":1,"column":"n","label":"n"}]"""), Results, At, out var reason);

        reason.ShouldBeNull();
        var node = JsonNode.Parse(json)!;
        node["ok"]!.GetValue<bool>().ShouldBeTrue();
        node["data"]!["kind"]!.GetValue<string>().ShouldBe("bar");
        node["data"]!["points"]!.GetValue<int>().ShouldBe(2);
        node["data"]!["series"]!.AsArray().Count.ShouldBe(2);
        node["data"]!["series"]![0]!["callIndex"]!.GetValue<int>().ShouldBe(1);
        json.ShouldNotContain("0.8");
    }

    [Theory]
    [InlineData("""[{"callIndex":9,"column":"n","label":"L"}]""", "series")] // unknown call
    [InlineData("""[{"callIndex":2,"column":"n","label":"L"}]""", "series")] // failed call
    [InlineData("""[{"callIndex":1,"column":"nope","label":"L"}]""", "series")] // missing column
    [InlineData("""[{"callIndex":1,"column":"note","label":"L"}]""", "series")] // not numeric
    [InlineData("""[{"callIndex":1,"column":"n","label":"<b>L</b>"}]""", "series")] // markup in a label
    [InlineData("""[{"callIndex":3,"column":"k","label":"L"}]""", "series")] // text column and different length
    [InlineData("""[]""", "series")]
    [InlineData("""[{"callIndex":1,"column":"n","label":"1"},{"callIndex":1,"column":"n","label":"2"},{"callIndex":1,"column":"n","label":"3"},{"callIndex":1,"column":"n","label":"4"},{"callIndex":1,"column":"n","label":"5"}]""", "series")] // five series
    public void A_bad_series_is_refused_and_named(string y, string which)
    {
        var json = ChartTool.Run(Args(y), Results, At, out var reason);

        reason.ShouldBe(which);
        var node = JsonNode.Parse(json)!;
        node["ok"]!.GetValue<bool>().ShouldBeFalse();
        node["error"]!["code"]!.GetValue<string>().ShouldBe("chart.bad_reference");
        node["error"]!["message"]!.GetValue<string>().ShouldStartWith(which);
    }

    [Fact]
    public void A_bad_title_kind_x_or_arguments_is_refused()
    {
        ChartTool.Run(Args(title: new string('t', 81)), Results, At, out var r1).ShouldContain("chart.bad_reference");
        r1.ShouldBe("title");
        ChartTool.Run(Args(title: "<i>x</i>"), Results, At, out var r2).ShouldContain("chart.bad_reference");
        r2.ShouldBe("title");
        ChartTool.Run(Args(kind: "pie"), Results, At, out var r3).ShouldContain("chart.bad_reference");
        r3.ShouldBe("kind");
        ChartTool.Run(Args(x: """{"callIndex":1,"column":"nope"}"""), Results, At, out var r4).ShouldContain("chart.bad_reference");
        r4.ShouldBe("x");
        ChartTool.Run("not json", Results, At, out var r5).ShouldContain("chart.bad_reference");
        r5.ShouldBe("arguments");
    }

    [Fact]
    public void More_than_two_hundred_points_are_refused()
    {
        var rows = string.Join(",", Enumerable.Range(0, 201).Select(i => $$"""{"k":"{{i}}","v":{{i}}}"""));
        var results = new Dictionary<int, string> { [1] = $$"""{"ok":true,"rows":[{{rows}}]}""" };

        ChartTool.Run("""{"kind":"line","title":"T","x":{"callIndex":1,"column":"k"},"y":[{"callIndex":1,"column":"v","label":"V"}]}""", results, At, out var reason).ShouldContain("200 points");
        reason.ShouldBe("points");
    }

    [Fact]
    public void The_descriptor_names_the_tool_and_stays_small()
    {
        ChartTool.Descriptor.Name.ShouldBe("make_chart");
        ChartTool.Descriptor.Description.Split(' ').Length.ShouldBeLessThanOrEqualTo(40);
        JsonNode.Parse(ChartTool.Descriptor.InputSchemaJson).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Win rate 80%", "ok", "title")]
    [InlineData("T", "Rate 0.9", "series")]
    public void A_digit_in_the_title_or_a_label_is_refused_because_it_would_not_come_from_a_tool(string title, string label, string which)
    {
        var args = Args($$"""[{"callIndex":1,"column":"n","label":"{{label}}"}]""", title: title);

        var json = ChartTool.Run(args, Results, At, out var reason);

        reason.ShouldBe(which);
        json.ShouldContain("chart.bad_reference");
    }
}

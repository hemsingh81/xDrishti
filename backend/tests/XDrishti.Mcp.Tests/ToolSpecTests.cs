using System.Text.Json;

namespace XDrishti.Mcp.Tests;

/// <summary>Spec P9-02 AC 6: a bad argument gives a message that names the property, never an exception; the schema is the check.</summary>
public sealed class ToolSpecTests
{
    private static readonly ToolSpec Spec = new(
        "t",
        "d",
        new ArgDef("sql", ArgType.String, true, "q", 1, 10),
        new ArgDef("maxRows", ArgType.Integer, false, "n", 1, 200),
        new ArgDef("name", ArgType.String, false, "p", 1, 63, "^[a-z0-9_]{1,63}$"));

    private static (ToolArguments? Arguments, string? Problem) Check(params (string Key, object? Value)[] given) =>
        Spec.Validate(given.Select(g => new KeyValuePair<string, object?>(g.Key, g.Value is null ? null : JsonSerializer.SerializeToElement(g.Value))));

    [Fact]
    public void Valid_arguments_are_accepted_and_typed()
    {
        var (arguments, problem) = Check(("sql", "select 1"), ("maxRows", 7), ("name", "sim_trades"));

        problem.ShouldBeNull();
        arguments!.String("sql").ShouldBe("select 1");
        arguments.Integer("maxRows").ShouldBe(7);
        arguments.String("name").ShouldBe("sim_trades");
    }

    [Fact]
    public void An_optional_argument_may_be_absent_or_null()
    {
        var (arguments, problem) = Check(("sql", "select 1"), ("maxRows", null));

        problem.ShouldBeNull();
        arguments!.Integer("maxRows").ShouldBeNull();
    }

    [Theory]
    [InlineData("sql", """{"maxRows":5}""")] // missing required
    [InlineData("extra", """{"sql":"select 1","extra":1}""")] // extra property
    [InlineData("sql", """{"sql":5}""")] // wrong type
    [InlineData("sql", """{"sql":""}""")] // too short
    [InlineData("sql", """{"sql":"01234567890"}""")] // too long
    [InlineData("maxRows", """{"sql":"x","maxRows":"5"}""")] // wrong type
    [InlineData("maxRows", """{"sql":"x","maxRows":5.5}""")] // not an integer
    [InlineData("maxRows", """{"sql":"x","maxRows":0}""")] // below
    [InlineData("maxRows", """{"sql":"x","maxRows":201}""")] // above
    [InlineData("name", """{"sql":"x","name":"accounts.broker_secrets"}""")] // pattern
    [InlineData("name", """{"sql":"x","name":"UPPER"}""")]
    public void A_bad_call_names_the_property(string expectedProperty, string json)
    {
        var given = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value));

        var (arguments, problem) = Spec.Validate(given);

        arguments.ShouldBeNull();
        problem.ShouldNotBeNull();
        problem.ShouldContain($"'{expectedProperty}'");
    }

    [Fact]
    public void An_unknown_property_is_named_cut_to_forty_characters()
    {
        var (_, problem) = Check(("sql", "x"), (new string('p', 200), 1));

        problem!.Length.ShouldBeLessThan(80);
    }

    [Fact]
    public void The_schema_forbids_extra_properties_lists_the_required_ones_and_carries_the_bounds()
    {
        var schema = Spec.Schema;

        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["sql"]);
        schema.GetProperty("properties").GetProperty("maxRows").GetProperty("maximum").GetInt32().ShouldBe(200);
        schema.GetProperty("properties").GetProperty("sql").GetProperty("maxLength").GetInt32().ShouldBe(10);
        schema.GetProperty("properties").GetProperty("name").GetProperty("pattern").GetString().ShouldBe("^[a-z0-9_]{1,63}$");
    }

    [Fact]
    public void The_four_tools_have_the_documented_names_and_arguments()
    {
        ListTablesTool.Spec.Name.ShouldBe("list_tables");
        DescribeTableTool.Spec.Name.ShouldBe("describe_table");
        RunSqlTool.Spec.Name.ShouldBe("run_sql");
        LabValidateTool.Spec.Name.ShouldBe("lab_validate");
        RunSqlTool.Spec.Arguments.Select(a => a.Name).ShouldBe(["sql", "maxRows"]);
        LabValidateTool.Spec.Arguments.Single().Max.ShouldBe(65_536);
        RunSqlTool.Spec.Arguments[0].Max.ShouldBe(4000);
    }

    private static readonly ToolSpec MapSpec = new(
        "m",
        "d",
        new ArgDef("flag", ArgType.Boolean, false, "f"),
        new ArgDef("query", ArgType.StringMap, false, "q", 0, 5, null, 2, ["a", "b", "c"]));

    private static (ToolArguments? Arguments, string? Problem) CheckMap(string json) =>
        MapSpec.Validate(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)));

    [Fact]
    public void A_map_and_a_boolean_are_typed_and_the_map_keys_counts_and_lengths_are_checked()
    {
        var (arguments, problem) = CheckMap("""{"flag":true,"query":{"a":"x","b":"yy"}}""");

        problem.ShouldBeNull();
        arguments!.Boolean("flag").ShouldBe(true);
        arguments.Map("query").ShouldBe(new Dictionary<string, string> { ["a"] = "x", ["b"] = "yy" });
        CheckMap("""{"query":{"z":"x"}}""").Problem!.ShouldContain("unknown key");
        CheckMap("""{"query":{"a":"x","b":"y","c":"z"}}""").Problem!.ShouldContain("more than 2 keys");
        CheckMap("""{"query":{"a":"123456"}}""").Problem!.ShouldContain("at most 5 characters");
        CheckMap("""{"query":{"a":1}}""").Problem!.ShouldContain("must be text");
        CheckMap("""{"query":[1]}""").Problem!.ShouldContain("'query'");
        CheckMap("""{"flag":"yes"}""").Problem!.ShouldContain("'flag'");
        var schema = MapSpec.Schema.GetProperty("properties").GetProperty("query");
        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("maxProperties").GetInt32().ShouldBe(2);
    }
}

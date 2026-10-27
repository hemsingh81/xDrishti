using System.ComponentModel.DataAnnotations;

namespace XDrishti.Mcp.Tests;

/// <summary>P9-10 review: two clients with the same token value stop the server from starting (a shared token cannot be revoked alone, and the gate would name the wrong client).</summary>
public sealed class McpOptionsTokenTests
{
    private static List<ValidationResult> Validate(Dictionary<string, string> tokens)
    {
        var options = new McpOptions { Tokens = tokens };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, true);
        return results;
    }

    [Fact]
    public void Distinct_tokens_and_empty_ones_are_accepted() =>
        Validate(new() { ["Api"] = "aaaa", ["Hermes"] = "bbbb", ["Eval"] = string.Empty, ["Other"] = string.Empty }).ShouldBeEmpty();

    [Fact]
    public void Two_clients_with_the_same_token_are_refused_and_the_value_is_not_in_the_message()
    {
        var results = Validate(new() { ["Api"] = "secret-value", ["Hermes"] = "secret-value", ["Eval"] = "other" });

        var message = results.ShouldHaveSingleItem().ErrorMessage!;
        message.ShouldContain("Api, Hermes");
        message.ShouldNotContain("secret-value");
    }
}

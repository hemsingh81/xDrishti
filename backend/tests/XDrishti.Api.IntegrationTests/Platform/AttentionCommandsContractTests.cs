using System.Text.Json.Nodes;
using XDrishti.Api.Infrastructure;
using XDrishti.Application.Portfolio.Attention;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>
/// The commands a notice may carry reach the client through the generated contract (ADR 0030): the document extension is built from the one C# list, every pattern is valid in .NET and has no construct that JavaScript lacks, and the contract file that
/// <c>scripts/gen-api.sh</c> writes carries exactly the list (so a changed list that was not regenerated fails here).
/// </summary>
public sealed class AttentionCommandsContractTests
{
    [Fact]
    public void The_extension_lists_every_allowed_command_with_its_method_template_and_pattern()
    {
        var document = AttentionCommandsContractDocument.Build();

        document.Select(n => (string?)n!["name"]).ShouldBe(AttentionCommands.Allowed.Select(c => c.Name));
        foreach (var (node, command) in document.Zip(AttentionCommands.Allowed))
        {
            ((string?)node!["method"], (string?)node["template"], (string?)node["pattern"]).ShouldBe((command.Method, command.Template, command.Pattern));
        }
    }

    [Fact]
    public void Every_pattern_compiles_is_anchored_and_uses_only_constructs_that_javascript_reads_the_same()
    {
        foreach (var command in AttentionCommands.Allowed)
        {
            System.Text.RegularExpressions.Regex.IsMatch(string.Empty, command.Pattern); // compiles
            command.Pattern.ShouldStartWith("^/api/");
            command.Pattern.ShouldEndWith("$");
            command.Pattern.ShouldNotContain("(?"); // no named groups, lookarounds or inline options
            command.Pattern.ShouldNotContain("\\z");
            command.Pattern.ShouldNotContain("\\A");
        }
    }

    [Fact]
    public void The_contract_file_carries_the_list_exactly()
    {
        var path = FindContract();
        var contract = JsonNode.Parse(File.ReadAllText(path))!;

        var written = contract[AttentionCommandsContractDocument.ExtensionName];

        written.ShouldNotBeNull($"{path} has no {AttentionCommandsContractDocument.ExtensionName}: run scripts/gen-api.sh");
        written.ToJsonString().ShouldBe(AttentionCommandsContractDocument.Build().ToJsonString(), "the contract is out of date: run scripts/gen-api.sh");
    }

    private static string FindContract()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "XDrishti.Api.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("backend/contracts/XDrishti.Api.json was not found above the test output.");
    }
}

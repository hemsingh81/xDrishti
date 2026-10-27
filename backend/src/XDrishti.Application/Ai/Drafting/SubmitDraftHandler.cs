using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// The one core of a draft (spec P9-07 D2): the Lab validator reads the text, then the checks C3 to C6 run. It references only the validator and the safe strategy reader: no model, no store, no Lab write. The dedicated job and the chat tool (a later slice) both end here, so the checks
/// are identical.
/// </summary>
internal sealed class SubmitDraftHandler(IStrategyChecker checker, IStrategyTextReader reader) : IQueryHandler<SubmitDraftQuery, DraftEvaluation>
{
    public ValueTask<DraftEvaluation> HandleAsync(SubmitDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var answer = query.Answer;
        var yaml = answer.Yaml;
        var hasYaml = !string.IsNullOrWhiteSpace(yaml);
        if (answer.Decline)
        {
            return ValueTask.FromResult(hasYaml
                ? new DraftEvaluation(DraftStatus.Invalid, yaml, [], [new DraftFinding(DraftCodes.DeclineWithYaml, string.Empty)], null)
                : new DraftEvaluation(DraftStatus.Declined, null, [], [], null));
        }

        if (!hasYaml)
        {
            return ValueTask.FromResult(new DraftEvaluation(DraftStatus.Invalid, null, [], [new DraftFinding(DraftCodes.YamlMissing, string.Empty)], null));
        }

        var text = yaml!;
        var check = Encoding.UTF8.GetByteCount(text) > LabErrors.MaxTextBytes ? null : checker.Check(text);
        IReadOnlyList<StrategyIssue> problems = check is null ? [LabErrors.TooLarge()] : check.Issues;
        var tree = check is null ? null : reader.ToJson(text);
        var facts = new DraftYamlFacts(text, tree, check?.Version, IdOf(tree), ParamCount(tree));
        var findings = check is null ? [] : DraftChecks.Run(query.Idea, answer, facts);
        var valid = check?.Description is not null;
        return ValueTask.FromResult(new DraftEvaluation(valid ? DraftStatus.Valid : DraftStatus.Invalid, text, problems, findings, check?.Description?.Id ?? IdOf(tree)));
    }

    private static string? IdOf(string? tree)
    {
        if (tree is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(tree);
        var root = DraftChecks.Strategy(document.RootElement);
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    }

    private static int ParamCount(string? tree)
    {
        if (tree is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(tree);
        var root = DraftChecks.Strategy(document.RootElement);
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object ? p.EnumerateObject().Count() : 0;
    }
}

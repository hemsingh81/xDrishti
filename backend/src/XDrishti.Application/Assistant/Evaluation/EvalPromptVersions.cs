using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The prompts of the assistant as the eval stamps them (spec P9-09 Scope): the list of the prompt registry. A new prompt, or a new version of one, changes the fingerprint.</summary>
internal sealed class EvalPromptVersions : IPromptVersions
{
    public IReadOnlyList<PromptStamp> All { get; } = [.. PromptRegistry.All.Select(p => new PromptStamp(p.Name, p.Version, p.Sha256))];
}

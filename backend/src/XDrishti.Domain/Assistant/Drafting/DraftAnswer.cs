namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>The model's answer, parsed and bounded (spec P9-07 Contract): decline with what is unsupported, or a strategy text with a plain list of what was understood and which values were chosen.</summary>
public sealed record DraftAnswer(
    bool Decline,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> Understood,
    IReadOnlyList<DraftAssumption> Assumptions,
    IReadOnlyList<DraftNotExpressed> NotExpressed,
    DraftRequestedTest RequestedTest,
    string? Yaml);

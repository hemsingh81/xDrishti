namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One mechanical assertion about a strategy draft (spec P9-09 "Other file formats"); the kinds are fixed.</summary>
public abstract record IdeaAssertion;

public sealed record PathEquals(string Path, string Value) : IdeaAssertion;

public sealed record PathExists(string Path) : IdeaAssertion;

public sealed record YamlContainsAny(IReadOnlyList<string> Words) : IdeaAssertion;

/// <summary>The number of the idea is in the YAML or listed in <c>not_expressed</c>.</summary>
public sealed record NumberCovered(string Token) : IdeaAssertion;

/// <summary>The model declined.</summary>
public sealed record DeclineAsserted : IdeaAssertion;

/// <summary>The decline's <c>unsupported</c> list or the draft's <c>not_expressed</c> list names one of the words.</summary>
public sealed record UnsupportedMentions(IReadOnlyList<string> Words) : IdeaAssertion;

public sealed record SidesInclude(IReadOnlyList<string> Sides) : IdeaAssertion;

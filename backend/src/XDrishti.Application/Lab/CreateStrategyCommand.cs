using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>Stores a strategy text as a new family or as the next version of one (spec P5-01). <paramref name="ParentHash"/> names the version it was cloned from; null means the previous latest version.</summary>
public sealed record CreateStrategyCommand(string Text, string? Note = null, string? ParentHash = null) : ICommand;

using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Evaluates one P15 family (P15-10): <c>filter:results</c>, <c>ext:ev-1</c>, <c>ext:fu-1</c>, <c>ext:llm-1</c> or <c>pead</c>, stores the verdicts and returns them. <see cref="HoldOut"/> is the explicit act that reads the reserved seasons, once, after a stored KEEP.</summary>
public sealed record EvaluateExtensionCommand(string Family, bool HoldOut) : ICommand;

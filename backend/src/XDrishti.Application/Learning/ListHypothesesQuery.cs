using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The newest view of each hypothesis, newest first (P6-10).</summary>
/// <param name="State">Only this state (proposed, approved, ...), or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListHypothesesQuery(string? State, int Limit) : IQuery<Result<IReadOnlyList<HypothesisView>>>;

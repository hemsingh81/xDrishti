using XDrishti.Application.Abstractions.Ai;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>The strategy text as plain JSON through the existing safe reader (limits, no anchors, aliases or tags). Stateless, no I/O.</summary>
internal sealed class StrategyTextReader : IStrategyTextReader
{
    public string? ToJson(string text) => StrategyTextJson.ToJson(text);
}

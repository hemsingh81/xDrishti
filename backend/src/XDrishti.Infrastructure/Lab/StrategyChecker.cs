using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Simulation.Replay;

namespace XDrishti.Infrastructure.Lab;

/// <summary>The strategy file library behind its port (spec P5-01): maps the plain values of the simulation library to the Application ones. Stateless, no I/O.</summary>
internal sealed class StrategyChecker : IStrategyChecker
{
    public StrategyCheck Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var check = StoredStrategies.Check(text);
        var description = check.Description is { } d ? new StrategyDescription(d.Id, d.Style, d.Hash, d.CanonicalJson, d.Timeframes, d.Sides, d.Parameters) : null;
        return new StrategyCheck(description, check.Version, [.. check.Issues.Select(i => new StrategyIssue(i.Path, i.Code, i.Message, i.Severity, i.Line, i.Column, i.EndLine, i.EndColumn, i.Start, i.Length, i.Suggestion))]);
    }

    public string? Retarget(string text, string newId) => StoredStrategies.Retarget(text, newId);

    public string SchemaJson() => StoredStrategies.SchemaJson();
}

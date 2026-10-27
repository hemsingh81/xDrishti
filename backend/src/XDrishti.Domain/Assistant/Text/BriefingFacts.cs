using System.Text;

namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// The only input of a briefing text (spec P9-06 D1): sentences built by the app from engine outputs, in a fixed order. The model phrases them, the guard checks the phrasing against them, the fallback prints them. Account names and per-account quantities are not here
/// unless the portfolio context switch was on when they were built. Pure.
/// </summary>
/// <param name="Kind"><c>daily</c> or <c>weekly</c>.</param>
/// <param name="Subject">The plan date (<c>2026-10-28</c>) or the ISO week (<c>2026-W43</c>).</param>
/// <param name="Provisional">Whether the numbers rest on cells that are not paper-proven (the text must then say so).</param>
/// <param name="Tickets">The primary tickets, then the reserve tickets: every one must appear in a daily text.</param>
/// <param name="Lines">The facts, in the order the fallback text prints them.</param>
/// <param name="PlanId">The plan behind a daily briefing.</param>
/// <param name="DigestId">The digest behind a weekly review.</param>
/// <param name="RunKeys">The report runs behind the insights of a weekly review.</param>
public sealed record BriefingFacts(string Kind, string Subject, bool Provisional, IReadOnlyList<string> Tickets, IReadOnlyList<FactLine> Lines, long? PlanId, long? DigestId, IReadOnlyList<string> RunKeys)
{
    public const string Daily = "daily";

    public const string Weekly = "weekly";

    /// <summary>Every fact in one block, one line each, as the model reads it (inside a fence the prompt names as data).</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var line in Lines)
        {
            builder.Append(line.Scope.Length == 0 ? "[all] " : $"[{line.Scope}] ").Append(line.Label).Append(" (").Append(line.State).Append("): ").Append(line.Text).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>Everything the facts say, as one string: what a symbol, number word, date or time of a text must be found in.</summary>
    public string AllText() => string.Join('\n', Lines.Select(l => l.Scope + " " + l.Label + " " + l.Text)) + "\n" + string.Join(' ', Tickets) + " " + Kind + " " + Subject;
}

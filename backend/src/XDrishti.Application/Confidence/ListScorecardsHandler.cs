using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The system card and the table of every Lab strategy's scorecard at its latest version, weakest first (spec CF-11 D5, ADR 0221). Derived on every request; read only. With no strategy the table is empty and the system card says so.
/// </summary>
internal sealed class ListScorecardsHandler(ScorecardReader reader, IReadinessSettings settings, ReadinessOptionsView options) : IQueryHandler<ListScorecardsQuery, Result<ScorecardListView>>
{
    public async ValueTask<Result<ScorecardListView>> HandleAsync(ListScorecardsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cards = await reader.AllAsync(cancellationToken);
        var system = await reader.SystemAsync(cards, cancellationToken);
        var current = await settings.CurrentAsync(cancellationToken);
        var rows = cards.Select(c => new ScorecardSummaryView(c.Strategy, c.Version, c.Score, c.Label, c.LabelText, c.WeakestSubScore, c.RowsPassed, c.HardFails, c.GateR.Passed, c.GateS.Passed)).ToList();
        return Result.Success(new ScorecardListView(system, rows, options.View(current), Statements(system)));
    }

    private static List<string> Statements(SystemScorecardView system)
    {
        var statements = new List<string>
        {
            "The scorecard is derived on every read from stored results; it raises no size and places no order. Unavailable is never a pass: a strategy with an input that does not exist reads Not ready until its producer exists and has run.",
            "The score is the minimum of the applicable sub-scores, not their mean, so one weak link caps it; a hard fail turns it into 0 and is named. Every threshold is a proposal and each snapshot stores the hash of the settings it used.",
            "Gate R (Candidate to Paper) and Gate S (Paper to Active) are checked when an owner moves a cell. With the enforce switch OFF (the default) a failed gate is shown as a warning and the move goes on; with it ON the move is refused.",
        };
        if (system.EqualWeights)
        {
            statements.Add("The system score weights the strategies equally because the plan does not expose a planned risk share per strategy.");
        }

        return statements;
    }
}

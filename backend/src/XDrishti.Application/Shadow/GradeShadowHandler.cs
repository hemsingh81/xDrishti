using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Grades the shadow record after the close (spec CF-08 part B, ADR 0212). It takes the candidates that count for edge and have no grade (newest session first, a bounded batch), replays each through <see cref="TicketReplayer"/> (the one path that grading and paper
/// capture use: no other code builds a simulation request) with the configured charge sheet, and inserts one grade each: graded with its R (net when the replay charged it, else gross, exactly the P7-13 grade's R) and, beside it, the same trade's gross R (the replay's filled R, which the grader itself uses when the trade is not charged) so that the summary can show gross figures while the sheet is unconfirmed, not triggered, or not gradable. A pending replay is
/// not stored and is tried again by the next run; a stored grade is never rewritten. With <c>paper.shadow.enabled</c> OFF nothing is read or written. It writes only <c>paper.shadow_grades</c>: no ticket moves, no order, no broker call.
/// </summary>
internal sealed class GradeShadowHandler(
    IShadowSettings settings,
    IShadowGradeStore store,
    IPlanStore plans,
    IGradeSettings grading,
    ICostSheetStore costSheets,
    ITradingCalendar calendars,
    TicketReplayer replayer,
    TimeProvider time,
    ILogger<GradeShadowHandler> logger) : ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>
{
    private const int MaxEntries = 300;

    private const string NotReplayed = "not_replayed";

    public async ValueTask<Result<ShadowGradeOutcome>> HandleAsync(GradeShadowCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await settings.IsEnabledAsync(cancellationToken))
        {
            ShadowLog.GradeSwitchedOff(logger);
            return Result.Success(new ShadowGradeOutcome(false, 0, 0, 0, 0, 0, 0));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(time.GetUtcNow());
        var costs = await ReplayCosts.ResolveAsync(grading, costSheets, cancellationToken);
        if (costs.IsFailure)
        {
            return Result.Failure<ShadowGradeOutcome>(PaperErrors.SheetMissing(grading.CostSheetId ?? string.Empty));
        }

        var due = await store.UngradedAsync(lastClosed, MaxEntries, cancellationToken);
        var items = new Dictionary<long, IReadOnlyList<PlanItemRow>>();
        var batch = new List<NewShadowGrade>();
        int pending = 0, failed = 0;
        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!items.TryGetValue(entry.PlanId, out var rows))
                {
                    items[entry.PlanId] = rows = await plans.ItemsAsync(entry.PlanId, cancellationToken);
                }

                if (rows.FirstOrDefault(r => r.Id == entry.PlanItemId) is not { } item)
                {
                    failed++; // the database holds the key onto the plan item, so this is not expected
                    continue;
                }

                var replay = await replayer.ReplayAsync(entry.SessionDate, lastClosed, calendar, item.Item, costs.Value.Choice, cancellationToken);
                if (replay.Grade is null)
                {
                    pending++;
                    continue;
                }

                batch.Add(Grade(entry.EntryId, replay, costs.Value));
            }
#pragma warning disable CA1031 // One bad candidate is counted as failed and the others still run; cancellation is rethrown by the filter. Only the count is logged: a simulator message can carry values.
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
            }
        }

        if (batch.Count > 0)
        {
            await store.InsertAsync(batch, cancellationToken);
        }

        var graded = batch.Count(g => g.Status == "graded");
        var notTriggered = batch.Count(g => g.Status == "not_triggered");
        var notGradable = batch.Count(g => g.Status == "not_gradable");
        ShadowLog.Graded(logger, due.Count, graded, notTriggered, notGradable, pending, failed);
        return Result.Success(new ShadowGradeOutcome(true, due.Count, graded, notTriggered, notGradable, pending, failed));
    }

    private static NewShadowGrade Grade(long entryId, TicketReplay replay, ResolvedCosts costs)
    {
        var grade = replay.Grade!;
        var engine = replay.EngineVersion ?? NotReplayed;
        if (grade.Status == GradeStatus.Graded && grade.R is { } r && replay.Trade is { } trade)
        {
            var net = trade.Costs == CostBasis.Charged && trade.Costing is not null; // the grader's own condition for "charged"
            return new NewShadowGrade(
                entryId, "graded", r, net ? "net" : "gross", trade.RFilled, DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpochSeconds), DateTimeOffset.FromUnixTimeSeconds(trade.ExitEpochSeconds), grade.ExitReason, engine, net ? costs.Sheet?.Id : null);
        }

        return new NewShadowGrade(entryId, grade.Status == GradeStatus.NotTriggered ? "not_triggered" : "not_gradable", null, null, null, null, null, null, engine, null);
    }
}

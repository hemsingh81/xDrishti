using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Stores the frozen-model counterfactual (spec P8-12, ADR 0110). It states the basis from the run headers first (gross and costed runs are never mixed; costed is net only on the owner's confirmed sheet), runs the existing evaluation and inserts the view of the
/// report once per input hash. A GET never reaches this: a model fit per window is a job, run by the verb. The only write is the report; nothing is applied.
/// </summary>
internal sealed class StoreCounterfactualHandler(
    IRunStore runs,
    IChargeSheetConfirmationSource confirmations,
    IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>> evaluate,
    ILearningReportStore store,
    TimeProvider time,
    ILogger<StoreCounterfactualHandler> logger) : ICommandHandler<StoreCounterfactualCommand, StoredCounterfactual>
{
    public async ValueTask<Result<StoredCounterfactual>> HandleAsync(StoreCounterfactualCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var query = command.Query;
        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<StoredCounterfactual>(LearningErrors.Invalid([new FieldError("actor", "The actor is required, at most 100 characters.")]));
        }

        if (query.Sources is not { Count: > 0 } sources || query.Counterfactual is null || query.Validation is null)
        {
            return Result.Failure<StoredCounterfactual>(LearningErrors.Invalid([new FieldError("sources", "Name at least one run; the options are required.")]));
        }

        var basis = await RunCostBasis.ResolveAsync(runs, confirmations, sources, cancellationToken);
        if (basis.IsFailure)
        {
            CounterfactualStoreLog.Refused(logger, basis.Error.Code);
            return Result.Failure<StoredCounterfactual>(basis.Error);
        }

        var evaluated = await evaluate.HandleAsync(query, cancellationToken);
        if (evaluated.IsFailure)
        {
            CounterfactualStoreLog.Refused(logger, evaluated.Error.Code);
            return Result.Failure<StoredCounterfactual>(evaluated.Error);
        }

        var report = evaluated.Value;
        var ordered = sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).ThenBy(s => s.Style, StringComparer.Ordinal).ThenBy(s => s.Exit, StringComparer.Ordinal).ToList();
        var text = report.ToText();
        var view = CounterfactualReportView.Of(report, query.HoldOutMonths, basis.Value, ordered, text);
        var row = new NewLearningReport(
            CounterfactualStore.Kind, CounterfactualStore.InputHash(query, report), view.Outcome, report.To, basis.Value, CounterfactualStore.Serialize(ordered),
            CounterfactualStore.Serialize(view.Knobs), CounterfactualStore.Serialize(view), text, command.Actor, time.GetUtcNow());
        var (stored, created) = await store.InsertAsync(row, cancellationToken);
        CounterfactualStoreLog.Stored(logger, view.Outcome, view.Windows.Count, created);
        return Result.Success(new StoredCounterfactual(stored.Id, created, stored.Report.Text));
    }
}

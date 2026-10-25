using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the stored study kinds (P8-13b; ids 4882 and 4883). Kind, flag and code only.</summary>
internal static partial class StoredReportLog
{
    [LoggerMessage(EventId = 4882, Level = LogLevel.Information, Message = "learning.report.stored {Kind}, created {Created}")]
    public static partial void Stored(ILogger logger, string kind, bool created);

    [LoggerMessage(EventId = 4883, Level = LogLevel.Warning, Message = "learning.report.store_refused {Kind}, {Code}")]
    public static partial void Refused(ILogger logger, string kind, string code);
}

/// <summary>
/// Stores the entry-window study once per input hash (spec P8-13 criterion 9, ADR 0112). A study fits per fold: it is a job, run by the verb, never by a GET. The only write is the report; no window is applied.
/// </summary>
internal sealed class StoreEntryWindowsHandler(
    IRunStore runs,
    IChargeSheetConfirmationSource confirmations,
    IQueryHandler<ProposeEntryWindowsQuery, Result<EntryWindowReport>> propose,
    ILearningReportStore store,
    TimeProvider time,
    ILogger<StoreEntryWindowsHandler> logger) : ICommandHandler<StoreEntryWindowsCommand, StoredReport>
{
    public async ValueTask<Result<StoredReport>> HandleAsync(StoreEntryWindowsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var query = command.Query;
        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("actor", "The actor is required, at most 100 characters.")]));
        }

        if (query.Sources is not { Count: > 0 } sources || query.Options is null || query.Validation is null)
        {
            return Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("sources", "Name at least one run; the options are required.")]));
        }

        var basis = await ReportBasis.ResolveAsync(runs, confirmations, sources, cancellationToken);
        if (basis.IsFailure)
        {
            StoredReportLog.Refused(logger, StoredReportKinds.EntryWindows, basis.Error.Code);
            return Result.Failure<StoredReport>(basis.Error);
        }

        var result = await propose.HandleAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            StoredReportLog.Refused(logger, StoredReportKinds.EntryWindows, result.Error.Code);
            return Result.Failure<StoredReport>(result.Error);
        }

        var report = result.Value;
        var ordered = sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).ThenBy(s => s.Style, StringComparer.Ordinal).ThenBy(s => s.Exit, StringComparer.Ordinal).ToList();
        var text = StoredReportKinds.TextOf(report.ToText());
        var view = EntryWindowReportView.Of(report, query.HoldOutMonths, basis.Value, ordered, text);
        var row = new NewLearningReport(
            StoredReportKinds.EntryWindows, StoredReportKinds.WindowsHash(query), view.Outcome, report.Rows == 0 ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime) : report.To, basis.Value,
            StoredReportKinds.Serialize(ordered), StoredReportKinds.Serialize(view.Knobs), StoredReportKinds.Serialize(view), text, command.Actor, time.GetUtcNow());
        var (stored, created) = await store.InsertAsync(row, cancellationToken);
        StoredReportLog.Stored(logger, StoredReportKinds.EntryWindows, created);
        return Result.Success(new StoredReport(stored.Id, created, stored.Report.Text));
    }
}

/// <summary>
/// Stores the meta-labelling policy evaluation once per input hash (spec P8-13 criterion 9, ADR 0112). A NoPolicy result is a result and is stored. The only write is the report; no threshold is applied.
/// </summary>
internal sealed class StoreMetaPolicyHandler(
    IRunStore runs,
    IChargeSheetConfirmationSource confirmations,
    IQueryHandler<EvaluateMetaPolicyQuery, Result<MetaPolicyReport>> evaluate,
    IModelTrainer trainer,
    ILearningReportStore store,
    TimeProvider time,
    ILogger<StoreMetaPolicyHandler> logger) : ICommandHandler<StoreMetaPolicyCommand, StoredReport>
{
    public async ValueTask<Result<StoredReport>> HandleAsync(StoreMetaPolicyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var query = command.Query;
        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("actor", "The actor is required, at most 100 characters.")]));
        }

        if (query.Sources is not { Count: > 0 } sources || query.Policy is null || query.Validation is null)
        {
            return Result.Failure<StoredReport>(LearningErrors.Invalid([new FieldError("sources", "Name at least one run; the options are required.")]));
        }

        var basis = await ReportBasis.ResolveAsync(runs, confirmations, sources, cancellationToken);
        if (basis.IsFailure)
        {
            StoredReportLog.Refused(logger, StoredReportKinds.MetaPolicy, basis.Error.Code);
            return Result.Failure<StoredReport>(basis.Error);
        }

        var result = await evaluate.HandleAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            StoredReportLog.Refused(logger, StoredReportKinds.MetaPolicy, result.Error.Code);
            return Result.Failure<StoredReport>(result.Error);
        }

        var report = result.Value;
        var ordered = sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).ThenBy(s => s.Style, StringComparer.Ordinal).ThenBy(s => s.Exit, StringComparer.Ordinal).ToList();
        var text = StoredReportKinds.TextOf(report.ToText());
        var view = MetaPolicyReportView.Of(report, query.HoldOutMonths, basis.Value, ordered, text);
        var recipe = $"{trainer.Candidate.Name} ({trainer.Parameters})";
        var row = new NewLearningReport(
            StoredReportKinds.MetaPolicy, StoredReportKinds.PolicyHash(query, report, recipe), view.Outcome, report.Rows == 0 ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime) : report.To, basis.Value,
            StoredReportKinds.Serialize(ordered), StoredReportKinds.Serialize(view.Knobs), StoredReportKinds.Serialize(view), text, command.Actor, time.GetUtcNow());
        var (stored, created) = await store.InsertAsync(row, cancellationToken);
        StoredReportLog.Stored(logger, StoredReportKinds.MetaPolicy, created);
        return Result.Success(new StoredReport(stored.Id, created, stored.Report.Text));
    }
}

/// <summary>One stored entry-window study (spec P8-13 criterion 8). Read only: it fits nothing.</summary>
internal sealed class GetEntryWindowsHandler(ILearningReportStore store) : IQueryHandler<GetEntryWindowsQuery, Result<EntryWindowReportView>>
{
    public async ValueTask<Result<EntryWindowReportView>> HandleAsync(GetEntryWindowsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await StoredReportKinds.ReadAsync<EntryWindowReportView>(
            store, StoredReportKinds.EntryWindows, query.Id, StoredReportKinds.EntryWindowsNotFoundCode, "entry-window study",
            (v, s) => v with { Id = s.Id, BuiltBy = s.Report.BuiltBy, CreatedAt = s.Report.CreatedAt }, cancellationToken);
    }
}

/// <summary>One stored meta-policy evaluation (spec P8-13 criterion 8). Read only: it fits nothing.</summary>
internal sealed class GetMetaPolicyHandler(ILearningReportStore store) : IQueryHandler<GetMetaPolicyQuery, Result<MetaPolicyReportView>>
{
    public async ValueTask<Result<MetaPolicyReportView>> HandleAsync(GetMetaPolicyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await StoredReportKinds.ReadAsync<MetaPolicyReportView>(
            store, StoredReportKinds.MetaPolicy, query.Id, StoredReportKinds.MetaPolicyNotFoundCode, "meta-policy evaluation",
            (v, s) => v with { Id = s.Id, BuiltBy = s.Report.BuiltBy, CreatedAt = s.Report.CreatedAt }, cancellationToken);
    }
}

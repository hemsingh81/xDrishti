using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Run the eval once (spec P9-09, <c>xd eval run</c>). <paramref name="Sets"/> are data, plans, ideas, events or all; <paramref name="Repeats"/> 1 to 5 (one repeat is a look only and can never be accepted). Only the owner's verb sends it: no endpoint, service or job does.
/// </summary>
public sealed record RunEvalCommand(IReadOnlyList<string> Sets, int Repeats, string Target, string Actor) : ICommand
{
    public const string Builtin = "builtin";
    public const int DefaultRepeats = 3;
    public const int MaxRepeats = 5;
}

/// <summary>Grade answers another assistant gave (spec <c>xd eval grade</c>, the Hermes PoC): a JSON-lines text of <c>{set, case_id, repeat, answer, session_id, started_at, finished_at}</c>. Data questions only; no model is called.</summary>
public sealed record GradeEvalAnswersCommand(string AnswersJsonl, string Target, string Actor) : ICommand;

/// <summary>Accept a run as the baseline (spec AC 11): the owner's act, refused unless the run is a gating PASS; a set that got worse needs a reason.</summary>
public sealed record AcceptEvalCommand(Guid RunId, string? Reason, string Actor) : ICommand;

/// <summary>Compare a run with a baseline (the newest accepted one when none is named), set by set, paired (spec AC 12).</summary>
public sealed record CompareEvalsQuery(Guid RunId, Guid? BaselineId) : IQuery<Result<EvalComparisonView>>;

/// <summary>One run with its cases.</summary>
public sealed record GetEvalQuery(Guid Id) : IQuery<Result<EvalDetailView>>;

/// <summary>The runs, newest first, 1 to 50.</summary>
public sealed record ListEvalsQuery(int Limit = ListEvalsQuery.DefaultLimit) : IQuery<Result<IReadOnlyList<EvalRunView>>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
}

/// <summary>Whether the accepted baseline still describes the assistant as it is now (spec AC 10). Reads files and the store only: no network call.</summary>
public sealed record GetEvalStatusQuery : IQuery<EvalStatusView>;

using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionReports;

/// <summary>The option archive per underlying.</summary>
public sealed record GetOptionArchiveSummaryQuery : IQuery<Result<OptionArchiveSummaryView>>;

/// <summary>The newest stored expression runs (default 20, at most 100), of one profile hash (64 lower-case hex characters) or of all.</summary>
public sealed record ListExpressionRunsQuery(string? Profile, int? Limit) : IQuery<Result<ExpressionRunList>>;

/// <summary>One stored expression run by id; unknown is <c>research.not_found</c>.</summary>
public sealed record GetExpressionRunQuery(string Id) : IQuery<Result<ExpressionRunSummary>>;

/// <summary>The stored pair formations whose window overlaps the range (default the last 400 days, at most 400).</summary>
public sealed record ListPairFormationsQuery(DateOnly? From, DateOnly? To) : IQuery<Result<PairFormationList>>;

/// <summary>The newest record of every P14 family; <paramref name="Cut"/> names families to show as cut (an unknown name is refused).</summary>
public sealed record GetP14VerdictListQuery(IReadOnlyList<string> Cut) : IQuery<Result<P14VerdictList>>;

/// <summary>The four summaries together, with the limits block and the printed form (the <c>xd reports options</c> verb).</summary>
public sealed record GetOptionsReportQuery(IReadOnlyList<string> Cut) : IQuery<Result<OptionsReportView>>;

/// <summary>The four summaries, the limits that always apply, and the text form of the P8 states.</summary>
public sealed record OptionsReportView(OptionsReportData Data, string Text);

/// <summary>The data of the report: what the routes show plus the limits block.</summary>
public sealed record OptionsReportData(OptionArchiveSummaryView Archive, ExpressionRunList ExpressionRuns, PairFormationList PairFormations, P14VerdictList Verdicts, IReadOnlyList<string> Limits);

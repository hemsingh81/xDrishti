using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>The id or profile hash shape of the stored runs: 64 lower-case hex characters.</summary>
internal static partial class RunIdShape
{
    public static bool IsValid(string? text) => text is not null && Hex().IsMatch(text);

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Hex();
}

/// <summary><c>GET /api/research/expression-runs</c> (spec P14-16): the newest stored runs, newest first, default 20, at most 100. A profile that is not 64 lower-case hex characters, or a limit outside 1 to 100, is <c>options.invalid_query</c>; nothing stored is an empty list.</summary>
internal sealed class ListExpressionRunsHandler(IOptionReportReader reader) : IQueryHandler<ListExpressionRunsQuery, Result<ExpressionRunList>>
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public async ValueTask<Result<ExpressionRunList>> HandleAsync(ListExpressionRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            return Result.Failure<ExpressionRunList>(OptionReportErrors.InvalidQuery($"limit is 1 to {MaxLimit}."));
        }

        if (query.Profile is not null && !RunIdShape.IsValid(query.Profile))
        {
            return Result.Failure<ExpressionRunList>(OptionReportErrors.InvalidQuery("profile is a 64-character lower-case hex hash."));
        }

        var runs = await reader.ExpressionRunsAsync(query.Profile, limit, cancellationToken);
        return Result.Success(runs.Count == 0
            ? new ExpressionRunList(ReportStates.Unavailable, ReportStates.Reasons.NoRuns, [])
            : new ExpressionRunList(ReportStates.Ok, null, [.. runs.Select(ExpressionRunMapper.ToSummary)]));
    }
}

/// <summary><c>GET /api/research/expression-runs/{id}</c> (spec P14-16): one stored run; an id that is not stored (or is not 64 hex characters) is <c>research.not_found</c>.</summary>
internal sealed class GetExpressionRunHandler(IOptionReportReader reader) : IQueryHandler<GetExpressionRunQuery, Result<ExpressionRunSummary>>
{
    public async ValueTask<Result<ExpressionRunSummary>> HandleAsync(GetExpressionRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = RunIdShape.IsValid(query.Id) ? await reader.ExpressionRunAsync(query.Id, cancellationToken) : null;
        return run is null ? Result.Failure<ExpressionRunSummary>(OptionReportErrors.RunNotFound()) : Result.Success(ExpressionRunMapper.ToSummary(run));
    }
}

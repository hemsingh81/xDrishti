using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The three basket values of <c>get_report</c> (spec BK-12): <c>basket_profile</c> and <c>basket_ranking</c> call the BK-07 handlers (the very ones of the basket screen) and only reshape their views; <c>basket_context</c> reads the stored row through <see cref="IAiDataReader"/>. Read only.
/// A host that does not register the BK-07 handlers answers <c>tool.unavailable</c> for the first two and the rest of the assistant is unaffected. An unknown or ambiguous basket is <c>basket.unknown</c> with up to five near names, cleaned like any text from the database.
/// </summary>
internal sealed class BasketReportReader(IServiceScopeFactory scopes, IAiDataReader data, TimeProvider time)
{
    public const string Profile = "basket_profile";
    public const string Ranking = "basket_ranking";
    public const string Context = "basket_context";
    public const string UnknownCode = "basket.unknown";

    public static bool Handles(string report) => report is Profile or Ranking or Context;

    public async Task<AiToolData> GetAsync(string report, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var refs = await data.GetBasketRefsAsync(cancellationToken);
        switch (report)
        {
            case Profile:
                var one = Resolve(Get(query, "basket"), refs);
                return one.Failure ?? await ProfileAsync(one.Id!.Value, query, cancellationToken);
            case Ranking:
                return await RankingAsync(query, refs, cancellationToken);
            case Context:
                var ctx = Resolve(Get(query, "basket"), refs);
                return ctx.Failure ?? await ContextAsync(ctx.Id!.Value, Get(query, "date"), cancellationToken);
            default:
                return AiToolData.Fail(ToolErrorCodes.InvalidArguments, "Unknown report kind.");
        }
    }

    private async Task<AiToolData> ProfileAsync(Guid id, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>>() is not { } handler)
        {
            return Unavailable();
        }

        var result = await handler.HandleAsync(new GetBasketProfileQuery(id.ToString("D"), Get(query, "from"), Get(query, "to"), Get(query, "index"), null), cancellationToken);
        return result.IsFailure ? Fail(result.Error) : BasketReportShaper.Profile(result.Value);
    }

    private async Task<AiToolData> RankingAsync(IReadOnlyDictionary<string, string> query, IReadOnlyList<AiBasketRef> refs, CancellationToken cancellationToken)
    {
        var names = (Get(query, "baskets") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length is < 2 or > 12)
        {
            return AiToolData.Fail(ToolErrorCodes.InvalidArguments, "baskets: give 2 to 12 basket names or ids, separated by commas.");
        }

        var ids = new List<Guid>(names.Length);
        foreach (var name in names)
        {
            var resolved = Resolve(name, refs);
            if (resolved.Failure is not null)
            {
                return resolved.Failure;
            }

            ids.Add(resolved.Id!.Value);
        }

        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>>() is not { } handler)
        {
            return Unavailable();
        }

        var result = await handler.HandleAsync(new GetBasketRankingQuery(string.Join(',', ids.Select(i => i.ToString("D"))), Get(query, "from"), Get(query, "to"), Get(query, "index")), cancellationToken);
        return result.IsFailure ? Fail(result.Error) : BasketReportShaper.Ranking(result.Value);
    }

    private async Task<AiToolData> ContextAsync(Guid id, string? dateText, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return AiToolData.Fail(ToolErrorCodes.InvalidArguments, "date: give a day such as 2026-10-30.");
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        return date > today
            ? AiToolData.Fail(ToolErrorCodes.InvalidArguments, "date is in the future; no context is stored for it.")
            : await data.GetBasketContextAsync(id, date, cancellationToken);
    }

    private static (Guid? Id, AiToolData? Failure) Resolve(string? text, IReadOnlyList<AiBasketRef> refs)
    {
        var match = BasketNameResolver.Resolve(text, refs);
        if (match.Id is { } id)
        {
            return (id, null);
        }

        // the near names are text from the database: cleaned and escaped before they reach the model (an error is not cleaned by the runner)
        var near = match.NearNames.Select(n => (string)CellCleaner.Clean(n, true, new CleanCounters())!).ToList();
        var message = near.Count == 0 ? "No basket has that name or id." : "No single basket has that name; near names (untrusted text): " + string.Join("; ", near);
        return (null, AiToolData.Fail(UnknownCode, message));
    }

    private static string? Get(IReadOnlyDictionary<string, string> query, string key) => query.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static AiToolData Fail(Error error) => AiToolData.Fail(error.Code, error.Message);

    private static AiToolData Unavailable() => AiToolData.Fail(ToolErrorCodes.Unavailable, "The basket reports are not available on this server.");
}

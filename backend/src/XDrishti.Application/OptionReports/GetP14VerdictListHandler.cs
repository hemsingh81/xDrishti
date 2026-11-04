using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>
/// The newest stored record of every P14 family that exists (spec P14-16): the expression families, the pairs family and the IV feature family. Reads one record per family through the evaluation read port. A family with no record is <c>none stored</c>; a family named in
/// <c>Cut</c> is <c>cut</c> and is not read. The basis families are not built and are not listed.
/// </summary>
internal sealed class GetP14VerdictListHandler(IExtensionEvaluationReader evaluations) : IQueryHandler<GetP14VerdictListQuery, Result<P14VerdictList>>
{
    public static IReadOnlyList<string> Families { get; } = [.. ExpressionFamilies.All, .. PairFamilies.All, .. OptionFeatureFamilies.All];

    public async ValueTask<Result<P14VerdictList>> HandleAsync(GetP14VerdictListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var unknown = query.Cut.Where(c => !Families.Contains(c, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure<P14VerdictList>(Error.Validation("options.unknown_family", "Unknown family in --cut: " + string.Join(", ", unknown.Select(Printable)) + ". Known: " + string.Join(", ", Families) + "."));
        }

        var items = new List<P14VerdictItem>(Families.Count);
        foreach (var family in Families)
        {
            if (query.Cut.Contains(family, StringComparer.Ordinal))
            {
                items.Add(new P14VerdictItem(family, ReportStates.Cut, null, null, null, null, null, null, null, [], []));
                continue;
            }

            var found = await evaluations.ListAsync(family, 1, cancellationToken);
            var newest = found.Count > 0 ? found[0] : null;
            items.Add(newest is null
                ? new P14VerdictItem(family, ReportStates.NoneStored, null, null, null, null, null, null, null, [], [])
                : new P14VerdictItem(family, ReportStates.Stored, newest.EvaluationId, newest.Protocol, newest.Verdict, newest.Trials, newest.FoldsEvaluable, newest.FoldsTotal, newest.HoldOutRead, newest.Missing, newest.Limits));
        }

        return Result.Success(new P14VerdictList(items));
    }

    private static string Printable(string text) => new([.. text.Take(40).Select(ch => char.IsControl(ch) ? '?' : ch)]);
}

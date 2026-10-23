using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>One registered model with what the registry keeps about it (spec P6-07).</summary>
internal sealed class GetModelHandler(IModelRegistry registry) : IQueryHandler<GetModelQuery, Result<ModelDetailView>>
{
    internal static readonly string[] FixedLimits =
    [
        "Advisory: no plan or order uses this model until P7; naming a champion changes nothing else.",
        "The verdict is on replayed history with modelled fills: out of sample for the model, but the data have been seen (ISS-19). Whether the model holds on real trades is not known.",
        "The hash proves that the file is the one that was stored, not who made it.",
    ];

    public async ValueTask<Result<ModelDetailView>> HandleAsync(GetModelQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var stored = await registry.FindAsync(query.Id, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<ModelDetailView>(ModelErrors.NotFound(query.Id));
        }

        var m = stored.Model;
        var designations = await registry.DesignationsAsync(query.Id, cancellationToken);
        var named = m.FeatureVersion == TradeFeatureCatalog.Version && m.Importance.Count == TradeFeatureCatalog.Names.Count;
        var importance = m.Importance
            .Select((v, i) => new FeatureImportanceView(named ? TradeFeatureCatalog.Names[i] : "f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), v))
            .OrderByDescending(f => f.MeanAbsContribution)
            .ThenBy(f => f.Feature, StringComparer.Ordinal)
            .ToList();
        var calibration = m.CalibrationScores.Select((s, i) => new CalibrationPoint(s, m.CalibrationValues[i])).ToList();
        List<string> limits = [.. FixedLimits];
        if (m.Note is not null)
        {
            limits.Add(m.Note);
        }

        return Result.Success(new ModelDetailView(
            ModelView.Of(stored),
            m.Parameters,
            m.ReportText,
            calibration,
            importance,
            [.. designations.Select(d => new DesignationView(d.Id, d.Role.Code(), d.Reason, d.CreatedAt, d.CreatedBy))],
            limits));
    }
}

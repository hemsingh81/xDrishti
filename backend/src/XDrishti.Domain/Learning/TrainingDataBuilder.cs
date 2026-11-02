using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Joins the labels of a <see cref="TrainingSet"/> to the stored decision-time features and the point-in-time cell features (P6-04, ADR 0066). The rule for unknowns: a labelled trade without a feature row, or with a required feature unknown (NaN or infinite), is
/// dropped and counted, never filled. Pure and deterministic.
/// </summary>
public static class TrainingDataBuilder
{
    /// <summary>Joins the labels to the features.</summary>
    /// <param name="set">The labelled set.</param>
    /// <param name="stored">The stored feature rows of the runs of the set (values in catalog order, <see cref="TradeFeatureCatalog.StoredCount"/> each).</param>
    /// <param name="cells">The cell features by trade key (<see cref="TradeFeatureCatalog.CellCount"/> values each), from <see cref="PointInTimeCells"/>.</param>
    /// <param name="required">The names a row must have known; null means <see cref="TradeFeatureCatalog.DefaultRequired"/>.</param>
    /// <param name="provenance">Text that is part of the id besides the data: the sources and the cell-statistics options, so the same rows built with other knobs are another set.</param>
    public static Result<TrainingData> Build(TrainingSet set, IReadOnlyList<TradeFeatureRow> stored, IReadOnlyDictionary<string, double[]> cells, IReadOnlyList<string>? required = null, string? provenance = null) =>
        BuildCore(set, stored, cells, required, provenance, null);

    /// <summary>
    /// The same join with one extension group appended (P13-05): the extension values follow the <see cref="TradeFeatureCatalog.All"/> values of each row. A labelled trade without an extension row is dropped and counted (<see cref="TrainingDataExtension.DroppedNoExtension"/>), a required
    /// extension value unknown drops it (counted per name), any other unknown stays NaN, never filled. Without a group, use <see cref="Build(TrainingSet, IReadOnlyList{TradeFeatureRow}, IReadOnlyDictionary{string, double[]}, IReadOnlyList{string}?, string?)"/>: its rows and id do not change.
    /// </summary>
    /// <param name="set">The labelled set.</param>
    /// <param name="stored">The stored feature rows.</param>
    /// <param name="cells">The cell features by trade key.</param>
    /// <param name="extension">The extension group with its values by trade key.</param>
    /// <param name="required">The catalog names a row must have known; null means <see cref="TradeFeatureCatalog.DefaultRequired"/>.</param>
    /// <param name="provenance">Text that is part of the id besides the data.</param>
    public static Result<TrainingData> Build(TrainingSet set, IReadOnlyList<TradeFeatureRow> stored, IReadOnlyDictionary<string, double[]> cells, ExtensionGroupInput extension, IReadOnlyList<string>? required = null, string? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return BuildCore(set, stored, cells, required, provenance, extension);
    }

    private static Result<TrainingData> BuildCore(TrainingSet set, IReadOnlyList<TradeFeatureRow> stored, IReadOnlyDictionary<string, double[]> cells, IReadOnlyList<string>? required, string? provenance, ExtensionGroupInput? extension)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(cells);
        var names = (required ?? TradeFeatureCatalog.DefaultRequired).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var requiredIndex = new int[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            requiredIndex[i] = TradeFeatureCatalog.IndexOf(names[i]);
            if (requiredIndex[i] < 0)
            {
                return Result.Failure<TrainingData>(Error.Validation("training_data.unknown_feature", "The feature '" + names[i] + "' is not in catalog " + TradeFeatureCatalog.Version + "."));
            }
        }

        var byKey = new Dictionary<string, TradeFeatureRow>(stored.Count, StringComparer.Ordinal);
        foreach (var row in stored)
        {
            if (row.Values.Count != TradeFeatureCatalog.StoredCount)
            {
                return Result.Failure<TrainingData>(Error.Validation("training_data.bad_row", "A stored feature row of " + row.TradeKey + " does not have " + TradeFeatureCatalog.StoredCount.ToString(CultureInfo.InvariantCulture) + " values."));
            }

            if (!byKey.TryAdd(row.TradeKey, row))
            {
                return Result.Failure<TrainingData>(Error.Conflict("training_data.duplicate_key", "Two feature rows have the key " + row.TradeKey + "."));
            }
        }

        var unknown = names.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        string[] extNames = [];
        Dictionary<string, int> extUnknown = new(StringComparer.Ordinal);
        if (extension is not null)
        {
            var problem = extension.Problem();
            if (problem is not null)
            {
                return Result.Failure<TrainingData>(Error.Validation("training_data.extension", problem));
            }

            extNames = [.. extension.Group.Names];
            extUnknown = (extension.Required ?? []).Distinct(StringComparer.Ordinal).ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        }

        var extDroppedNoRow = 0;
        var rows = new List<TrainingRow>(set.Labels.Count);
        int noFeatures = 0, droppedUnknown = 0;
        DateOnly? from = null, to = null;
        foreach (var label in set.Labels)
        {
            if (!byKey.TryGetValue(label.Key, out var stor) || !cells.TryGetValue(label.Key, out var cell) || cell.Length != TradeFeatureCatalog.CellCount)
            {
                noFeatures++;
                continue;
            }

            double[]? ext = null;
            if (extension is not null)
            {
                if (!extension.Values.TryGetValue(label.Key, out ext))
                {
                    extDroppedNoRow++;
                    continue;
                }

                if (ext.Length != extNames.Length)
                {
                    return Result.Failure<TrainingData>(Error.Validation("training_data.bad_row", "An extension row of " + label.Key + " does not have " + extNames.Length.ToString(CultureInfo.InvariantCulture) + " values."));
                }
            }

            var values = new double[TradeFeatureCatalog.All.Count + extNames.Length];
            for (var i = 0; i < TradeFeatureCatalog.StoredCount; i++)
            {
                values[i] = stor.Values[i];
            }

            for (var i = 0; i < TradeFeatureCatalog.CellCount; i++)
            {
                values[TradeFeatureCatalog.StoredCount + i] = cell[i];
            }

            var complete = true;
            for (var i = 0; i < names.Length; i++)
            {
                if (!double.IsFinite(values[requiredIndex[i]]))
                {
                    unknown[names[i]]++;
                    complete = false;
                }
            }

            if (ext is not null)
            {
                for (var i = 0; i < ext.Length; i++)
                {
                    values[TradeFeatureCatalog.All.Count + i] = ext[i];
                }

                foreach (var name in extUnknown.Keys.Order(StringComparer.Ordinal))
                {
                    if (!double.IsFinite(ext[Array.IndexOf(extNames, name)]))
                    {
                        extUnknown[name]++;
                        complete = false;
                    }
                }
            }

            if (!complete)
            {
                droppedUnknown++;
                continue;
            }

            rows.Add(new TrainingRow(label.Key, stor.Strategy, stor.Timeframe, stor.Side, stor.TradeDate, stor.DecisionEpochSeconds, values, label.Label, label.R, label.SessionsHeld, label.ResolvedEpochSeconds));
            from = from is null || stor.TradeDate < from ? stor.TradeDate : from;
            to = to is null || stor.TradeDate > to ? stor.TradeDate : to;
        }

        if (rows.Count == 0 || from is null || to is null)
        {
            return Result.Failure<TrainingData>(Error.Validation("training_data.empty", "No labelled trade has all its required features known (" + noFeatures.ToString(CultureInfo.InvariantCulture) + " without features, " + droppedUnknown.ToString(CultureInfo.InvariantCulture) + " with one unknown)."));
        }

        var id = IdOf(set, names, rows, provenance, extension?.Group);
        var data = new TrainingData(id, set.Id, TradeFeatureCatalog.Version, set.RuleVersion, set.Basis, set.RunKeys, names, from.Value, to.Value, rows, set.Labels.Count, noFeatures, droppedUnknown, unknown);
        return Result.Success(extension is null ? data : data with { Extension = new TrainingDataExtension(extension.Group.Version, extNames, extDroppedNoRow, extUnknown) });
    }

    private static string IdOf(TrainingSet set, string[] names, List<TrainingRow> rows, string? provenance, ExtensionGroup? group)
    {
        var text = new StringBuilder();
        text.Append("training-data/1\n").Append(set.Id).Append('\n').Append(TradeFeatureCatalog.Version).Append('\n').Append(provenance).Append('\n');
        foreach (var name in names)
        {
            text.Append(name).Append(',');
        }

        text.Append('\n');
        if (group is not null)
        {
            text.Append("ext:").Append(group.Version).Append(':').Append(string.Join(',', group.Names)).Append('\n');
        }

        foreach (var row in rows)
        {
            text.Append(row.TradeKey);
            foreach (var v in row.Values)
            {
                text.Append('|').Append(double.IsNaN(v) ? "nan" : v.ToString("R", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

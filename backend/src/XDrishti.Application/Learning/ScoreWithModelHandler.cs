using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Scores a feature vector with a registered model (spec P6-07, ADR 0070). The file is read through the file port, which refuses bytes that no longer hash to the recorded SHA-256, and loaded through the scorer port, which checks the hash again: a model file
/// is never trusted without its recorded hash.
/// </summary>
internal sealed class ScoreWithModelHandler(IModelRegistry registry, IModelFiles files, IModelScorerLoader loader, ILogger<ScoreWithModelHandler> logger) : IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>>
{
    public async ValueTask<Result<ModelScoreView>> HandleAsync(ScoreWithModelQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Top is < 0 or > 50 || query.Features is null)
        {
            return Result.Failure<ModelScoreView>(LearningErrors.Invalid([new FieldError("top", "The number of contributions is 0 to 50, and the features are required.")]));
        }

        var stored = await registry.FindAsync(query.ModelId, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<ModelScoreView>(ModelErrors.NotFound(query.ModelId));
        }

        var m = stored.Model;
        if (query.Features.Count != m.FeatureWidth)
        {
            return Result.Failure<ModelScoreView>(LearningErrors.Invalid([new FieldError("features", string.Create(CultureInfo.InvariantCulture, $"The model reads {m.FeatureWidth} features, {query.Features.Count} were given."))]));
        }

        var bytes = await files.ReadAsync(m.Sha256, cancellationToken);
        if (bytes.IsFailure)
        {
            ModelLog.FileRefused(logger, query.ModelId, bytes.Error.Code);
            return Result.Failure<ModelScoreView>(bytes.Error);
        }

        var loaded = loader.Load(bytes.Value, m.Sha256);
        if (loaded.IsFailure)
        {
            ModelLog.FileRefused(logger, query.ModelId, loaded.Error.Code);
            return Result.Failure<ModelScoreView>(loaded.Error);
        }

        var row = new ValidationRow("score", 0, 0, default, 0, 0, string.Empty, [.. query.Features]);
        var named = m.FeatureVersion == TradeFeatureCatalog.Version && m.FeatureWidth == TradeFeatureCatalog.Names.Count;
        var contributions = loaded.Value.Explain(row, query.Top)
            .Select(c => new FeatureImportanceView(named ? TradeFeatureCatalog.Names[c.Index] : "f" + c.Index.ToString(CultureInfo.InvariantCulture), c.Value))
            .ToList();
        var probability = loaded.Value.Score(row);
        ModelLog.Scored(logger, query.ModelId);
        return Result.Success(new ModelScoreView(query.ModelId, probability, contributions));
    }
}

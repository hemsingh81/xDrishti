using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Scores one feature vector with a registered model (P6-07): for experiments and, later, P7. Advisory: nothing acts on the result.</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Features">The features in the order of the model's feature catalog (NaN where unknown; the model imputes its fit mean and the explanation then shows that mean).</param>
/// <param name="Top">How many contributions to return, 0 to 50.</param>
public sealed record ScoreWithModelQuery(long ModelId, IReadOnlyList<double> Features, int Top) : IQuery<Result<ModelScoreView>>;

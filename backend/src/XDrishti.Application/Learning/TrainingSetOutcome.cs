namespace XDrishti.Application.Learning;

/// <summary>What a training-set build did.</summary>
/// <param name="Id">The id of the training data.</param>
/// <param name="Rows">Examples kept.</param>
/// <param name="Positives">Examples with label 1.</param>
/// <param name="Negatives">Examples with label 0.</param>
/// <param name="Unresolved">Trades without a label because the bars ended while open.</param>
/// <param name="NoTarget">Trades without a label for want of a target level.</param>
/// <param name="DroppedNoFeatures">Labelled trades with no stored features (run the extraction first).</param>
/// <param name="DroppedUnknown">Labelled trades with a required feature unknown.</param>
/// <param name="UnknownByFeature">How many labelled trades had each required feature unknown, features with none left out.</param>
/// <param name="AlreadyStored">The identical record was there; nothing was written.</param>
public sealed record TrainingSetOutcome(string Id, int Rows, int Positives, int Negatives, int Unresolved, int NoTarget, int DroppedNoFeatures, int DroppedUnknown, IReadOnlyDictionary<string, int> UnknownByFeature, bool AlreadyStored);

using XDrishti.Indicators;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>A candidate exit policy as compiled: the policy, its parameters by name as numbers (parameter names resolved; a default the policy has, such as <c>atr_period</c> 14, included) and the features it reads on the evaluation timeframe.</summary>
public sealed record ExitPlan(ExitPolicy Policy, IReadOnlyDictionary<string, IReadOnlyList<double>> Values, IReadOnlyList<FeatureSpec> Reads);

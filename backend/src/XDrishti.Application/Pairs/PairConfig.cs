using System.Globalization;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Pairs;

/// <summary>The settings hash a pair trade carries as <c>cfg:&lt;hash&gt;</c>: the pre-registered options, the notional, the charge basis and the fill rules. A run and an evaluation have to agree on it, or the evaluation does not judge the trade.</summary>
internal static class PairConfig
{
    public static string Of(PairStudyOptions options, CostSheet? sheet) =>
        ExtensionFamilies.Sha256Hex(string.Join('|', options.Canonical(), options.PairNotional.ToString(CultureInfo.InvariantCulture), sheet?.RatesSha256 ?? "gross", FillRules.Default.ToCanonicalJson()))[..8];
}

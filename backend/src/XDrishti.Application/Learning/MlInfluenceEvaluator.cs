using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Gathers what the predicate needs about a registered model and evaluates it (CF-10 part 10c-2, ADR 0217). Read only. The registry gives the model, the hold-out ledger gives where it stands with its one look, the evidence port gives the figures; whatever is not available is passed
/// as such, which the predicate counts as false.
/// </summary>
internal sealed class MlInfluenceEvaluator(IModelRegistry registry, ModelHoldOutLooks looks, IMlInfluenceEvidence evidence, IMlInfluenceSwitch influenceSwitch, MlInfluenceOptions options)
{
    /// <summary>The evaluation, or null when no model has this id.</summary>
    public async ValueTask<MlInfluenceEvaluation?> EvaluateAsync(long modelId, CancellationToken cancellationToken)
    {
        var stored = await registry.FindAsync(modelId, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var reading = await looks.ReadAsync(stored, cancellationToken);
        var known = await evidence.ReadAsync(stored, cancellationToken);
        var switchOn = await influenceSwitch.IsOnAsync(cancellationToken);

        // the figures of the hold-out count only for the model that owns a fresh look; for any other reading they are dropped, so a spent hold-out never carries numbers
        var holdOut = reading.Facts;
        if (reading.State == HoldOutReading.Used && holdOut is not null && known.HoldOut is { } figures)
        {
            holdOut = holdOut with { Rows = figures.Rows, ProbabilityBeatsNoMl = figures.ProbabilityBeatsNoMl, PolicyEffectR = figures.PolicyEffectR };
        }

        var facts = new MlInfluenceFacts(holdOut, known.Forward, known.CalibrationAlarm, switchOn);
        var hash = Hash(options.KeyText() + "|switch=" + (switchOn ? "on" : "off"));
        return new MlInfluenceEvaluation(stored, reading with { Facts = holdOut }, facts, MlInfluence.Evaluate(facts, options), hash);
    }

    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

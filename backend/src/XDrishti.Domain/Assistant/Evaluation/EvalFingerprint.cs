using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The fingerprint of a run (spec P9-09 AC 10): SHA-256 over the model file hash, the runtime image digest, every prompt version and hash, the sets, the thresholds, the fixture and the profile. It is computed without any network call, so a changed prompt version or
/// model makes the accepted baseline stale at once; <see cref="Differences"/> names what differs. Pure.
/// </summary>
public static class EvalFingerprint
{
    public static string Of(FingerprintInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var text = new StringBuilder();
        text.Append("model=").Append(inputs.ModelSha256).Append('\n');
        text.Append("runtime=").Append(inputs.RuntimeDigest).Append('\n');
        foreach (var prompt in inputs.Prompts.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            text.Append("prompt=").Append(prompt.Name).Append(':').Append(prompt.Version).Append(':').Append(prompt.Sha256).Append('\n');
        }

        text.Append("sets=").Append(inputs.SetsHash).Append('\n');
        text.Append("thresholds=").Append(inputs.ThresholdsHash).Append('\n');
        text.Append("fixture=").Append(inputs.FixtureHash).Append('\n');
        text.Append("profile=").Append(inputs.Profile).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>What differs between the current inputs and the baseline's, in words: model, runtime, prompt X (changed, added or removed), sets, thresholds, fixture, profile.</summary>
    public static IReadOnlyList<string> Differences(FingerprintInputs current, FingerprintInputs baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        var reasons = new List<string>();
        if (current.ModelSha256 != baseline.ModelSha256)
        {
            reasons.Add("model");
        }

        if (current.RuntimeDigest != baseline.RuntimeDigest)
        {
            reasons.Add("runtime");
        }

        foreach (var name in current.Prompts.Select(p => p.Name).Union(baseline.Prompts.Select(p => p.Name)).Order(StringComparer.Ordinal))
        {
            var now = current.Prompts.FirstOrDefault(p => p.Name == name);
            var then = baseline.Prompts.FirstOrDefault(p => p.Name == name);
            if (now != then)
            {
                reasons.Add("prompt " + name);
            }
        }

        if (current.SetsHash != baseline.SetsHash)
        {
            reasons.Add("sets");
        }

        if (current.ThresholdsHash != baseline.ThresholdsHash)
        {
            reasons.Add("thresholds");
        }

        if (current.FixtureHash != baseline.FixtureHash)
        {
            reasons.Add("fixture");
        }

        if (current.Profile != baseline.Profile)
        {
            reasons.Add("profile");
        }

        return reasons;
    }
}

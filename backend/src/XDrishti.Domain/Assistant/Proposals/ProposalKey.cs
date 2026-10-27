using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// The dedupe key of a proposal (spec P9-08 AC 7): SHA-256 (hex) over the kind, the subject and the identity fields of the payload, each normalised so that the same idea in another case or number form is the same key (<c>2.50</c> and <c>2.5</c>; a symbol in either case). A rephrased
/// knob value is a different key (honest limit).
/// </summary>
public static class ProposalKey
{
    public static string Hypothesis(HypothesisPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Hash(ProposalKind.Hypothesis, payload.CellKey.Trim().ToLowerInvariant(), payload.KnobName.Trim().ToLowerInvariant(), KnobBounds.Normal(payload.CandidateValue) ?? payload.CandidateValue.Trim());
    }

    public static string Basket(string kind, BasketPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Hash(kind, payload.BasketId.ToString("D"), payload.Symbol.Trim().ToUpperInvariant());
    }

    public static string Of(string kind, ProposalPayload payload) => payload switch
    {
        HypothesisPayload h => Hypothesis(h),
        BasketPayload b => Basket(kind, b),
        _ => throw new ArgumentException("Unknown payload.", nameof(payload)),
    };

    private static string Hash(params string[] parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts))));
}

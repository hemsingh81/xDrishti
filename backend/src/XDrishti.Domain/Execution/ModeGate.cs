using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Execution;

public enum GateOutcome
{
    Pass,
    Fail,
    Unavailable,
}

/// <summary>One fact of a gate with its words. <see cref="N"/> and <see cref="Needed"/> are the number found and the number the gate asks for, when the fact is a number.</summary>
public sealed record GateCheck(string Code, GateOutcome Outcome, string Text, int? N = null, int? Needed = null)
{
    public string OutcomeName => Outcome switch
    {
        GateOutcome.Pass => "pass",
        GateOutcome.Fail => "fail",
        _ => "unavailable",
    };
}

/// <summary>What the gate from <c>manual_live</c> to <c>assisted</c> reads. A null is a fact that could not be read: it never passes.</summary>
public sealed record ModeGateFacts(
    bool? ExecutionEnabled,
    bool? TradingRoleOn,
    bool? ConnectionConnected,
    string? LivePhaseVerdict,
    DateTimeOffset? SandboxSuitePassedAt,
    DateTimeOffset? StaticIpAttestedAt);

/// <summary>The verdict: eligible only when every check passed. <see cref="Hash"/> is the SHA-256 of the canonical text of the checks and the policy version.</summary>
public sealed record ModeGateResult(bool Eligible, IReadOnlyList<GateCheck> Checks, string Hash)
{
    public IReadOnlyList<string> Reasons => [.. Checks.Where(c => c.Outcome != GateOutcome.Pass).Select(c => c.Text)];
}

/// <summary>
/// The evidence gate of a promotion (spec P12-07, policy version 1). The numbers are constants here, not settings: loosening a gate is a code change with an ADR. Every check is listed whether it passed or not, and an unreadable fact is
/// <c>unavailable</c>, which is never a pass. Only <c>manual_live</c> to <c>assisted</c> exists in this build; every other pair is one failed check that says why.
/// </summary>
public static class ModeGate
{
    public const int PolicyVersion = 1;

    public const int SandboxMaxAgeDays = 30;

    public const int AttestationMaxAgeDays = 30;

    public const string LiveVerdictMet = "MET";

    public static ModeGateResult Evaluate(LadderMode from, LadderMode to, ModeGateFacts facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        List<GateCheck> checks;
        if (!ExecutionModes.IsAdjacentPromotion(from, to))
        {
            checks = [new GateCheck("not_adjacent", GateOutcome.Fail, "A promotion moves one step up at a time.")];
        }
        else if (from != LadderMode.ManualLive)
        {
            checks = [new GateCheck("not_gated_here", GateOutcome.Fail, "Paper to manual_live is changed in the account's Trading settings; it is not part of this ladder.")];
        }
        else
        {
            checks =
            [
                Flag("execution_enabled", facts.ExecutionEnabled, "The global execution switch is ON.", "The global execution switch is OFF."),
                Flag("trading_role", facts.TradingRoleOn, "The account's Trading role is on.", "The account's Trading role is off."),
                Flag("connection", facts.ConnectionConnected, "The Dhan connection is connected.", "The Dhan connection is not connected."),
                Verdict(facts.LivePhaseVerdict),
                Age("sandbox_suite", facts.SandboxSuitePassedAt, now, SandboxMaxAgeDays, "The sandbox suite passed", "the sandbox suite result (P12-02)"),
                Age("static_ip_attestation", facts.StaticIpAttestedAt, now, AttestationMaxAgeDays, "The static-IP attestation was made", "the static-IP attestation (P12-11)"),
            ];
        }

        return new ModeGateResult(checks.All(c => c.Outcome == GateOutcome.Pass), checks, HashOf(checks));
    }

    public static string HashOf(IReadOnlyList<GateCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var text = new StringBuilder("policy=").Append(PolicyVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var c in checks)
        {
            text.Append('\n').Append(c.Code).Append('|').Append(c.OutcomeName).Append('|').Append(c.N?.ToString(CultureInfo.InvariantCulture)).Append('|').Append(c.Needed?.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static GateCheck Flag(string code, bool? value, string pass, string fail) =>
        value switch
        {
            true => new GateCheck(code, GateOutcome.Pass, pass),
            false => new GateCheck(code, GateOutcome.Fail, fail),
            _ => new GateCheck(code, GateOutcome.Unavailable, "Not readable: " + code.Replace('_', ' ') + "."),
        };

    private static GateCheck Verdict(string? verdict) =>
        verdict switch
        {
            null => new GateCheck("live_phase_check", GateOutcome.Unavailable, "Not readable: the small-live phase check (P11-06)."),
            LiveVerdictMet => new GateCheck("live_phase_check", GateOutcome.Pass, "The small-live phase check says MET."),
            _ => new GateCheck("live_phase_check", GateOutcome.Fail, "The small-live phase check says " + verdict + ", not MET."),
        };

    private static GateCheck Age(string code, DateTimeOffset? at, DateTimeOffset now, int maxDays, string what, string noun)
    {
        if (at is not { } recorded)
        {
            return new GateCheck(code, GateOutcome.Unavailable, "No record: " + noun + " is not on record.", null, maxDays);
        }

        var days = (int)Math.Floor((now - recorded).TotalDays);
        return days is >= 0 && days <= maxDays
            ? new GateCheck(code, GateOutcome.Pass, what + " " + days.ToString(CultureInfo.InvariantCulture) + " days ago (at most " + maxDays.ToString(CultureInfo.InvariantCulture) + ").", days, maxDays)
            : new GateCheck(code, GateOutcome.Fail, what + " " + days.ToString(CultureInfo.InvariantCulture) + " days ago; at most " + maxDays.ToString(CultureInfo.InvariantCulture) + " counts.", days, maxDays);
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Execution;
using XDrishti.Domain.Market;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application.Readiness;

/// <summary>
/// The readiness probe (spec P12-11, ADR 0164). For every account at assisted or above, on trading days between 08:00 and 16:30 India time, it reads the stored connection, the broker's profile and its configured static IP (nothing else: the two calls of <see cref="IBrokerFacts"/>),
/// the attestation and the halts, judges them with <see cref="ReadinessPolicy"/> and stores a row when the verdict or a reason changed and at least every 15 minutes. At 09:10:30 it stores the day state once per account and session (late and unavailable when the service
/// was not running then). A verdict that is ready lets the halts <c>token_invalid</c> and <c>ip_not_allowed</c> clear; nothing else is touched. It sends no order and holds no token. Every failure is the unavailable side.
/// </summary>
public sealed partial class ReadinessLoop(
    IReadinessAccounts accounts, IReadinessStore store, IConnectionFacts connections, IBrokerFacts broker, IHaltStore halts, IReadinessSettings settings, TimeProvider clock,
    HaltAutoClearService? autoClear = null, ILogger<ReadinessLoop>? logger = null)
{
    private readonly ILogger<ReadinessLoop> _log = logger ?? NullLogger<ReadinessLoop>.Instance;

    /// <summary>Runs one pass. <paramref name="force"/> probes every account now (the owner's check now) but never outside the hours. Returns the number of rows written.</summary>
    public async Task<int> RunOnceAsync(bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ist = IndiaTime.ToIst(now);
        var session = DateOnly.FromDateTime(ist.DateTime);
        if (TimeOnly.FromDateTime(ist.DateTime) < ReadinessSchedule.WindowStart || !await accounts.IsTradingDayAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var written = 0;
        foreach (var account in await accounts.AssistedAccountsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                written += await RunAccountAsync(account, session, now, force, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var who = Hash(account);
                var kind = e.GetType().Name;
                LogFailed(_log, who, kind);
            }
        }

        return written;
    }

    /// <summary>Records at once that the broker rejected the token or the IP (P12-02 reasons <c>token_invalid</c> and <c>ip_not_allowed</c>), without waiting for the next probe. Any other reason is ignored. The rejection holds until a fresh profile read (token) or a fresh attestation (IP).</summary>
    public async Task<bool> NoteRejectionAsync(Guid accountId, string brokerReason, CancellationToken cancellationToken)
    {
        var code = brokerReason switch { "ip_not_allowed" => ReadinessPolicy.IpRejected, "token_invalid" => ReadinessPolicy.TokenRejected, _ => null };
        if (code is null)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var last = await store.LastCheckAsync(accountId, cancellationToken).ConfigureAwait(false);
        var text = code == ReadinessPolicy.IpRejected ? "An order was rejected for an invalid IP: check the line and the address at Dhan, then record a new attestation." : "The broker rejected the token: connect Dhan again.";
        List<ReadinessReason> reasons = [.. (last?.Reasons ?? []).Where(r => r.Code != code), new(code, ReadinessOutcome.Fail, code, text, null, now)];
        await store.AppendCheckAsync(new(now, accountId, IndiaTime.Today(now), ReadinessState.NotReady, reasons, null, null, null, null), cancellationToken).ConfigureAwait(false);
        var rejectedBy = Hash(accountId);
        LogRejection(_log, rejectedBy, code);

        return true;
    }

    private async Task<int> RunAccountAsync(Guid account, DateOnly session, DateTimeOffset now, bool force, CancellationToken cancellationToken)
    {
        var written = 0;
        var markNeeded = ReadinessDayMark.IsDue(now, session) && !await store.HasDayStateAsync(account, session, cancellationToken).ConfigureAwait(false);
        var late = markNeeded && ReadinessDayMark.IsLate(now, session);
        var last = await store.LastCheckAsync(account, cancellationToken).ConfigureAwait(false);
        ReadinessState? probed = null;
        if (ReadinessSchedule.InWindow(now) && ProbeWanted(now, last, force, markNeeded && !late))
        {
            var (record, state, stored) = await ProbeAsync(account, session, now, last, cancellationToken).ConfigureAwait(false);
            last = record ?? last;
            probed = state;
            written += stored ? 1 : 0;
        }

        if (markNeeded)
        {
            var decision = ReadinessDayMark.Decide(now, session, probed ?? ReadinessState.Unavailable);
            var reasons = decision.Late || probed is null ? (IReadOnlyList<ReadinessReason>)[new("probe", ReadinessOutcome.Unavailable, decision.Late ? "late_start" : "not_probed", "No readiness check was made at 09:10:30.")] : last?.Reasons ?? [];
            if (await store.AppendDayStateAsync(new(account, session, decision.State, reasons, last?.Id, decision.Late, now), cancellationToken).ConfigureAwait(false))
            {
                written++;
                var markedFor = Hash(account);
                var word = ReadinessWords.Word(decision.State);
                LogDayState(_log, markedFor, word, decision.Late);
            }
        }

        return written;
    }

    private bool ProbeWanted(DateTimeOffset now, ReadinessCheckRecord? last, bool force, bool markNow)
    {
        if (force || markNow || last is null)
        {
            return true;
        }

        var interval = ReadinessSchedule.Interval(now, last.Verdict == ReadinessState.Ready, settings.Current()?.IntervalSeconds ?? 300);
        return now - last.At >= interval;
    }

    private async Task<(ReadinessCheckRecord? Record, ReadinessState State, bool Stored)> ProbeAsync(Guid account, DateOnly session, DateTimeOffset now, ReadinessCheckRecord? last, CancellationToken cancellationToken)
    {
        ReadinessVerdict verdict;
        DateTimeOffset? tokenEnd = null;
        DateTimeOffset? profileAt = null;
        DateTimeOffset? ipAt = null;
        DateOnly? modifyAfter = null;
        var config = settings.Current();
        IpAttestation? attestation = null;
        if (config is null)
        {
            verdict = ReadinessPolicy.Unjudged("settings_unavailable");
        }
        else
        {
            try
            {
                var connection = await ReadOrNull(() => connections.ReadAsync(account, cancellationToken)).ConfigureAwait(false);
                var profile = await broker.GetProfileAsync(account, cancellationToken).ConfigureAwait(false);
                var ip = await broker.GetStaticIpAsync(account, cancellationToken).ConfigureAwait(false);
                var after = clock.GetUtcNow();
                var profileReading = new Reading<ProfileFacts>(profile.Readable, profile.Value, profile.Reason, profile.Readable ? after : null);
                var ipReading = new Reading<IpFacts>(ip.Readable, ip.Value, ip.Reason, ip.Readable ? after : null);
                attestation = IpLiteral.TryNormalize(config.ExpectedIp, out var expected) ? await store.NewestAttestationAsync(account, expected, cancellationToken).ConfigureAwait(false) : null;
                var rejections = (last?.Reasons ?? []).Where(r => r.Code is ReadinessPolicy.IpRejected or ReadinessPolicy.TokenRejected && r.Since is not null).Select(r => new Rejection(r.Code, r.Since!.Value)).ToList();
                var other = await OtherHaltAsync(account, cancellationToken).ConfigureAwait(false);
                verdict = ReadinessPolicy.Evaluate(new(after, connection, profileReading, ipReading, attestation, other, rejections, config));
                tokenEnd = connection?.ExpiresAt;
                profileAt = profileReading.At;
                ipAt = ipReading.At;
                modifyAfter = ip.Value?.ModifyAfter;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                verdict = ReadinessPolicy.Unjudged("probe_failed");
                var who = Hash(account);
                var kind = e.GetType().Name;
                LogFailed(_log, who, kind);
            }
        }

        var changed = last is null || !verdict.SameAs(last.Verdict, last.Reasons);
        ReadinessCheckRecord? record = null;
        var stored = false;
        if (ReadinessSchedule.ShouldStore(changed, last?.At, now))
        {
            record = await store.AppendCheckAsync(new(now, account, session, verdict.State, verdict.Reasons, tokenEnd, profileAt, ipAt, modifyAfter), cancellationToken).ConfigureAwait(false);
            stored = true;
            var storedFor = Hash(account);
            var storedWord = ReadinessWords.Word(verdict.State);
            var codes = string.Join(',', verdict.Reasons.Where(r => r.Outcome != ReadinessOutcome.Pass).Select(r => r.Detail));
            LogStored(_log, storedFor, storedWord, codes);
        }

        await ClearReadyHaltsAsync(account, verdict.State, attestation, cancellationToken).ConfigureAwait(false);
        return (record, verdict.State, stored);
    }

    private static async Task<T?> ReadOrNull<T>(Func<Task<T?>> read)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // True when a halt other than the two this slice clears is active for the account or the system; null when the halt record cannot be read.
    private async Task<bool?> OtherHaltAsync(Guid account, CancellationToken cancellationToken)
    {
        try
        {
            var active = await halts.ActiveAsync(cancellationToken).ConfigureAwait(false);
            return active.Any(h => (h.Scope == HaltScope.System || h.AccountId == account) && h.TriggerCode is not (HaltTriggers.TokenInvalid or HaltTriggers.IpNotAllowed));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // The P12-05 contract: token_invalid and ip_not_allowed clear when the verdict is ready; a verdict that is not ready (or unavailable) restarts nothing and clears nothing.
    private async Task ClearReadyHaltsAsync(Guid account, ReadinessState state, IpAttestation? attestation, CancellationToken cancellationToken)
    {
        if (autoClear is null)
        {
            return;
        }

        try
        {
            foreach (var halt in await halts.ActiveAsync(cancellationToken).ConfigureAwait(false))
            {
                if (halt.AccountId == account && halt.TriggerCode is HaltTriggers.TokenInvalid or HaltTriggers.IpNotAllowed)
                {
                    // An ip_not_allowed halt clears only on an attestation made after it was raised (criterion 8): an older attestation inside its age limit is not enough.
                    var attestedSince = halt.TriggerCode != HaltTriggers.IpNotAllowed || (attestation is { OrdersAllowed: true } && attestation.At > halt.At);
                    bool? good = state == ReadinessState.Ready ? attestedSince ? true : null : state == ReadinessState.NotReady ? false : null;
                    await autoClear.ObserveAsync(halt, good, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // the halt record could not be read: nothing is cleared
        }
    }

    private static string Hash(Guid account) => Convert.ToHexStringLower(SHA256.HashData(account.ToByteArray()))[..8];

    [LoggerMessage(EventId = 6740, Level = LogLevel.Information, Message = "execution.readiness account={Account} verdict={Verdict} reasons={Reasons}")]
    private static partial void LogStored(ILogger logger, string account, string verdict, string reasons);

    [LoggerMessage(EventId = 6741, Level = LogLevel.Warning, Message = "execution.readiness_failed account={Account} kind={Kind}; the verdict is unavailable")]
    private static partial void LogFailed(ILogger logger, string account, string kind);

    [LoggerMessage(EventId = 6742, Level = LogLevel.Information, Message = "execution.readiness_day_state account={Account} state={State} late={Late}")]
    private static partial void LogDayState(ILogger logger, string account, string state, bool late);

    [LoggerMessage(EventId = 6743, Level = LogLevel.Warning, Message = "execution.readiness_rejection account={Account} code={Code}")]
    private static partial void LogRejection(ILogger logger, string account, string code);
}

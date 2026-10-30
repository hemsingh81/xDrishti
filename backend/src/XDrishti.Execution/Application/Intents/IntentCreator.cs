using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Application.Intents;

/// <summary>What one pass did.</summary>
public sealed record IntentPass(int Created, int Armed, int Checking, int Cancelled, int Skipped);

/// <summary>
/// The intent creator (spec P12-08 criterion 16, P12-08a). One pass: every owner-approved plan ticket with an allocation for an account whose mode routes an entry (<c>semi_auto</c> waits for the tap, <c>auto_live</c> acts; <see cref="ModeRouting"/>)
/// becomes one intent in state planned (the table's unique key makes a second pass a no-op); a planned intent is armed once its ticket is armed, from 09:10 India time of its session; an armed intent whose trigger the live price reaches
/// (<see cref="TriggerWatcher"/>) moves to checking once, inside the entry window, with the account ready (P12-11), not halted (P12-05) and still allowed to act; otherwise it is cancelled with the reason (no catch-up). A trigger already
/// run past by more than P12-03's collar when the intent is created is cancelled <c>void</c>. It writes only <c>execution.intents</c> and <c>execution.events</c>, never a <c>trading.*</c> row; it holds no broker port and sends nothing.
/// Composed by no host: the mode source is closed and no account can be <c>semi_auto</c> or <c>auto_live</c> yet (ADR 0163).
/// </summary>
public sealed partial class IntentCreator(
    IIntentPlanSource plans, IIntentRecord record, IExecEventStore events, IModeSource modes, IHaltSource halts, IReadinessReads readiness, IIntentQuotes quotes, IIntentSettings settings, ExecEnvironment environment, TimeProvider clock,
    ILogger<IntentCreator>? logger = null)
{
    public const string Actor = "system:intent-creator";

    private readonly ILogger<IntentCreator> _log = logger ?? NullLogger<IntentCreator>.Instance;

    /// <summary>Runs one pass; null when the settings, the plan record or the stored intents cannot be read (nothing is created or moved).</summary>
    public async Task<IntentPass?> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (settings.Current() is not { } s)
        {
            LogPassSkipped(_log, "settings_unavailable");
            return null;
        }

        var now = clock.GetUtcNow().ToUniversalTime(); // the record stores UTC instants only
        var today = IndiaTime.Today(now);
        if (await plans.CandidatesAsync(today, cancellationToken).ConfigureAwait(false) is not { } candidates)
        {
            LogPassSkipped(_log, "plan_unreadable");
            return null;
        }

        IReadOnlyList<IntentRow> stored;
        try
        {
            stored = await record.SinceAsync(today.AddDays(-7), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Without the stored intents a pass could not tell a new ticket from one it already holds: it does nothing.
            LogPassSkipped(_log, "intents_unreadable");
            return null;
        }

        var existing = stored.Select(i => (i.AccountId, i.PlanItemId)).ToHashSet();
        var byKey = candidates.GroupBy(c => (c.AccountId, c.PlanItemId)).ToDictionary(g => g.Key, g => g.First());
        var pass = new Counter();
        var answers = new Dictionary<Guid, ModeAnswer?>();
        var halt = await ReadHaltAsync(cancellationToken).ConfigureAwait(false);

        foreach (var candidate in byKey.Values.Where(c => !existing.Contains((c.AccountId, c.PlanItemId))))
        {
            await GuardAsync(candidate.AccountId, () => CreateAsync(candidate, s, now, today, answers, pass, cancellationToken)).ConfigureAwait(false);
        }

        foreach (var intent in stored)
        {
            byKey.TryGetValue((intent.AccountId, intent.PlanItemId), out var candidate);
            await GuardAsync(intent.AccountId, () => AdvanceAsync(intent, candidate, s, now, today, halt, answers, pass, cancellationToken)).ConfigureAwait(false);
        }

        return new IntentPass(pass.Created, pass.Armed, pass.Checking, pass.Cancelled, pass.Skipped);
    }

    private async Task CreateAsync(PlanEntryCandidate c, IntentSettings s, DateTimeOffset now, DateOnly today, Dictionary<Guid, ModeAnswer?> answers, Counter pass, CancellationToken cancellationToken)
    {
        if (c.ItemState is not (TicketState.Approved or TicketState.Armed) || c.Session < today)
        {
            return;
        }

        var answer = await AnswerAsync(c.AccountId, answers, cancellationToken).ConfigureAwait(false);
        var side = c.Side == "BUY" ? ExecSide.Buy : ExecSide.Sell; // anything but BUY is refused: entries are BUY only until P12-13
        var why = Refusal(answer, side);
        if (why is null && (c.Entry is not > 0 || c.Stop is not > 0 || c.Target is not > 0))
        {
            why = IntentReasons.LevelsMissing;
        }

        if (why is null && string.IsNullOrWhiteSpace(c.SecurityId))
        {
            why = IntentReasons.InstrumentUnknown;
        }

        if (why is not null || c.Quantity <= 0)
        {
            pass.Skipped++;
            var skippedFor = Hash(c.AccountId);
            LogSkipped(_log, skippedFor, c.PlanItemId, why ?? IntentReasons.LevelsMissing);
            return;
        }

        var quantity = IntentRules.Quantity(c.Quantity, s.CanaryPending);
        var draft = new IntentDraft(c.AccountId, c.PlanId, c.PlanItemId, c.ApprovalId, c.Session, c.Symbol, c.SecurityId!, quantity, c.Entry!.Value, c.Stop!.Value, c.Target!.Value, environment, Actor);
        if (await record.CreateAsync(draft, cancellationToken).ConfigureAwait(false) is not { } intent)
        {
            return;
        }

        pass.Created++;
        var created = Hash(intent.PublicId);
        var owner = Hash(c.AccountId);
        LogCreated(_log, created, owner, c.PlanItemId, quantity, s.CanaryPending);

        // A trigger the price has already run past by more than the collar when the intent is made can only be chased: the intent is closed void at once.
        if (c.Session == today && await quotes.LastAsync(c.Symbol, cancellationToken).ConfigureAwait(false) is { } price && IntentRules.IsFresh(price, now, s.QuoteMaxAge)
            && TriggerWatcher.PassedBeyond(side, intent.Entry, price.Last, s.CollarPct))
        {
            await MoveAsync(intent, ExecState.Cancelled, IntentReasons.Void, now, pass, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AdvanceAsync(
        IntentRow intent, PlanEntryCandidate? c, IntentSettings s, DateTimeOffset now, DateOnly today, HaltReading halt, Dictionary<Guid, ModeAnswer?> answers, Counter pass, CancellationToken cancellationToken)
    {
        if (intent.State is not (ExecState.Planned or ExecState.Armed))
        {
            return;
        }

        var closed = c?.ItemState switch
        {
            TicketState.Invalidated => IntentReasons.PlanInvalidated,
            TicketState.Expired => IntentReasons.PlanExpired,
            _ => IntentRules.WindowOver(now, intent.Session, s) ? IntentReasons.Window : null,
        };
        if (closed is not null)
        {
            await MoveAsync(intent, ExecState.Cancelled, closed, now, pass, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (intent.Session != today)
        {
            return;
        }

        if (intent.State == ExecState.Planned)
        {
            if (c?.ItemState is (TicketState.Armed or TicketState.Triggered) && IntentRules.MayArm(now, intent.Session))
            {
                await MoveAsync(intent, ExecState.Armed, IntentReasons.Armed, now, pass, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (await quotes.LastAsync(intent.Symbol, cancellationToken).ConfigureAwait(false) is not { } price || !IntentRules.IsFresh(price, now, s.QuoteMaxAge)
            || !TriggerWatcher.TriggerCrossed(intent.Side, intent.Entry, price.Last))
        {
            return;
        }

        var answer = await AnswerAsync(intent.AccountId, answers, cancellationToken).ConfigureAwait(false);
        var check = await readiness.LastCheckAsync(intent.AccountId, cancellationToken).ConfigureAwait(false);
        var reason = IntentRules.OnCrossing(
            HaltPolicy.EntriesBlocked(halt),
            Refusal(answer, intent.Side) is null,
            IntentRules.IsReady(check?.Verdict, check?.SessionDate, check?.At, intent.Session, now),
            IntentRules.InWindow(now, intent.Session, s));
        await MoveAsync(intent, reason is null ? ExecState.Checking : ExecState.Cancelled, reason ?? IntentReasons.Triggered, now, pass, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null when the account may hold an entry intent: the account gate is open (an answer, the switch on, a sending mode, an environment) and the mode routes a BUY entry to a tap or to the system.</summary>
    private string? Refusal(ModeAnswer? answer, ExecSide side)
    {
        var gate = AccountGate.MayAct(answer, environment);
        if (!gate.Open)
        {
            return gate.Reason;
        }

        var route = ModeRouting.Decide(answer!.EffectiveMode, ActionClass.Entry, side);
        return route.Kind is RouteKind.AwaitConfirm or RouteKind.Automatic ? null : route.Code ?? "mode_not_executing";
    }

    private async Task MoveAsync(IntentRow intent, ExecState to, string reason, DateTimeOffset now, Counter pass, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["reason"] = reason });
        var moved = await events.AppendAsync(intent.Id, new ExecEvent(intent.Seq + 1, ExecEventKind.State, intent.State, to, ExecEventSource.Engine, Actor, now, 0, payload), cancellationToken).ConfigureAwait(false);
        var who = Hash(intent.PublicId);
        if (moved.IsFailure)
        {
            // Another pass moved it first (the chain refuses a second event with the same number): nothing to do.
            LogMoveRefused(_log, who, moved.Error.Code);
            return;
        }

        switch (to)
        {
            case ExecState.Armed:
                pass.Armed++;
                break;
            case ExecState.Checking:
                pass.Checking++;
                break;
            default:
                pass.Cancelled++;
                break;
        }

        var account = Hash(intent.AccountId);
        var from = intent.State.ToString().ToLowerInvariant();
        var into = to.ToString().ToLowerInvariant();
        LogMoved(_log, who, account, from, into, reason);
    }

    private async Task<ModeAnswer?> AnswerAsync(Guid account, Dictionary<Guid, ModeAnswer?> answers, CancellationToken cancellationToken)
    {
        if (!answers.TryGetValue(account, out var answer))
        {
            answer = await modes.AnswerAsync(account, cancellationToken).ConfigureAwait(false);
            answers[account] = answer;
        }

        return answer;
    }

    private async Task<HaltReading> ReadHaltAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await halts.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return HaltReading.Unreadable;
        }
    }

    private async Task GuardAsync(Guid account, Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var who = Hash(account);
            var kind = e.GetType().Name;
            LogFailed(_log, who, kind);
        }
    }

    private static string Hash(Guid id) => Convert.ToHexStringLower(SHA256.HashData(id.ToByteArray()))[..8];

    private sealed class Counter
    {
        public int Created { get; set; }

        public int Armed { get; set; }

        public int Checking { get; set; }

        public int Cancelled { get; set; }

        public int Skipped { get; set; }
    }

    [LoggerMessage(EventId = 6710, Level = LogLevel.Information, Message = "execution.intent_created intent={Intent} account={Account} plan_item={PlanItem} qty={Quantity} canary={Canary}")]
    private static partial void LogCreated(ILogger logger, string intent, string account, long planItem, int quantity, bool canary);

    [LoggerMessage(EventId = 6711, Level = LogLevel.Information, Message = "execution.intent_moved intent={Intent} account={Account} from={From} to={To} reason={Reason}")]
    private static partial void LogMoved(ILogger logger, string intent, string account, string from, string to, string reason);

    [LoggerMessage(EventId = 6712, Level = LogLevel.Debug, Message = "execution.intent_skipped account={Account} plan_item={PlanItem} reason={Reason}")]
    private static partial void LogSkipped(ILogger logger, string account, long planItem, string reason);

    [LoggerMessage(EventId = 6713, Level = LogLevel.Warning, Message = "execution.intent_pass_skipped reason={Reason}; nothing was created or moved")]
    private static partial void LogPassSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 6714, Level = LogLevel.Information, Message = "execution.intent_move_refused intent={Intent} code={Code}")]
    private static partial void LogMoveRefused(ILogger logger, string intent, string code);

    [LoggerMessage(EventId = 6715, Level = LogLevel.Warning, Message = "execution.intent_failed account={Account} kind={Kind}; this account is skipped in this pass")]
    private static partial void LogFailed(ILogger logger, string account, string kind);
}

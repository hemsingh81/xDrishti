using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Execution;

/// <summary>Reads the status of the execution service (spec P12-01b, criterion 12): read only, from the heartbeat and the stored record. It changes nothing and instructs nothing.</summary>
public sealed record GetExecutionStatusQuery : IQuery<ExecutionStatusView>;

/// <param name="Service">The service as the heartbeat shows it.</param>
/// <param name="Environment">The service's process environment from its heartbeat: <c>none</c>, <c>sandbox</c> or <c>live</c>; <c>none</c> when it never reported.</param>
/// <param name="Accounts">Every account with its stored mode and its gate. The gate is closed for all of them until the mode ladder exists (P12-07).</param>
/// <param name="Halts">The halts in force. Always empty in this slice (the halt store comes with P12-05) and <paramref name="EntriesBlocked"/> says so.</param>
/// <param name="OpenIntents">Intents not yet in a final state.</param>
/// <param name="SettingsHash">Hash of the settings in force as the service reports it, null when it never reported.</param>
/// <param name="EntriesBlocked">True while no entry may leave: always true in this slice (recovery and the halt store do not exist yet).</param>
/// <param name="AnyAutomated">True while execution is enabled and an account's mode sends orders without the owner typing them (semi_auto or auto_live). No account can be there today.</param>
/// <param name="KillSwitchActive">True while any halt is in force (spec P12-10).</param>
/// <param name="PollSeconds">How often the bell asks for notices: 5 while something is automated and the market is open, else 60.</param>
/// <param name="MarketOpen">True inside today's session in India time.</param>
public sealed record ExecutionStatusView(ExecutionServiceView Service, string Environment, IReadOnlyList<ExecutionAccountView> Accounts, IReadOnlyList<string> Halts, int OpenIntents, string? SettingsHash, bool EntriesBlocked, bool AnyAutomated = false, bool KillSwitchActive = false, int PollSeconds = 60, bool MarketOpen = false);

/// <param name="State"><c>recovering</c>, <c>ready</c>, <c>halted</c> or <c>down</c> (no heartbeat, or one older than 30 seconds).</param>
/// <param name="LastBeatAt">The last heartbeat, null when there never was one.</param>
public sealed record ExecutionServiceView(string State, DateTimeOffset? LastBeatAt);

public sealed record ExecutionGateView(bool Open, string Reason);

public sealed record ExecutionAccountView(Guid AccountId, string Mode, ExecutionGateView Gate);

/// <summary>What the record says; the handler turns it into the view.</summary>
public sealed record ExecutionFacts(string? HeartbeatState, DateTimeOffset? LastBeatAt, string? Environment, string? SettingsHash, int OpenIntents, IReadOnlyList<(Guid AccountId, string Mode)> Accounts, bool HaltActive = false, bool ExecutionEnabled = false);

public interface IExecutionFactsReader
{
    Task<ExecutionFacts> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class GetExecutionStatusHandler(IExecutionFactsReader reader, ITradingCalendar calendar, TimeProvider clock) : IQueryHandler<GetExecutionStatusQuery, ExecutionStatusView>
{
    public static readonly TimeSpan DownAfter = TimeSpan.FromSeconds(30);

    private static readonly string[] Known = ["recovering", "ready", "halted"];

    public async ValueTask<ExecutionStatusView> HandleAsync(GetExecutionStatusQuery query, CancellationToken cancellationToken)
    {
        var facts = await reader.ReadAsync(cancellationToken);
        var fresh = facts.LastBeatAt is { } beat && (clock.GetUtcNow() - beat).Duration() <= DownAfter; // a beat from the future (clock skew) is judged by the same distance
        // An unknown or missing word never reads as healthy: only a fresh heartbeat with a known state is anything but down.
        var state = fresh && facts.HeartbeatState is { } word && Known.Contains(word) ? word : "down";
        // The gate needs the mode ladder (P12-07): until it answers, every account's gate is closed with the same reason the service's closed default gives.
        var accounts = facts.Accounts.Select(a => new ExecutionAccountView(a.AccountId, a.Mode, new ExecutionGateView(false, "no_mode_answer"))).ToList();
        var now = clock.GetUtcNow();
        var ist = IndiaTime.ToIst(now);
        var minute = ist.Hour * 60 + ist.Minute;
        var open = (await calendar.GetAsync(cancellationToken)).SessionOf(IndiaTime.Today(now)) is { } session && minute >= session.FromMinute && minute < session.ToMinute;
        var automated = facts.ExecutionEnabled && facts.Accounts.Any(a => a.Mode is "semi_auto" or "auto_live");
        return new ExecutionStatusView(new ExecutionServiceView(state, facts.LastBeatAt), fresh ? facts.Environment ?? "none" : "none", accounts, [], facts.OpenIntents, fresh ? facts.SettingsHash : null, EntriesBlocked: true,
            AnyAutomated: automated, KillSwitchActive: facts.HaltActive, PollSeconds: automated && open ? 5 : 60, MarketOpen: open);
    }
}

using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Readiness;

/// <summary>India time (UTC+05:30, no daylight saving) for the readiness tests. 2026-10-27 is a Tuesday.</summary>
internal static class Ist
{
    public static DateTimeOffset At(int day, int hour, int minute, int second = 0, int month = 10) => new(2026, month, day, hour, minute, second, TimeSpan.FromMinutes(330));
}

/// <summary>One fake for every readiness port, so a test reads like a scene: the connection, the broker's two answers, the attestation, the settings and the calendar are plain fields.</summary>
internal sealed class MemReadiness : IReadinessStore, IReadinessAccounts, IConnectionFacts, IReadinessSettings, IBrokerFacts
{
    public const string Ip = "203.0.113.7";

    public static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public List<ReadinessCheckRecord> Checks { get; } = [];

    public List<DayStateDraft> Days { get; } = [];

    public List<IpAttestation> Attestations { get; } = [];

    public List<Guid> Assisted { get; } = [Account];

    public bool TradingDay { get; set; } = true;

    public ConnectionFacts? Connection { get; set; } = new("Connected", Ist.At(27, 17, 30));

    public ReadinessSettings? Settings { get; set; } = new(new TimeOnly(16, 45), 20, Ip, 30, 300);

    public BrokerRead<ProfileFacts> Profile { get; set; } = BrokerRead.Ok(new ProfileFacts(Ist.At(27, 17, 30)));

    public BrokerRead<IpFacts> StaticIp { get; set; } = BrokerRead.Ok(new IpFacts(Ip, null, new DateOnly(2026, 10, 1), null));

    public bool BrokerThrows { get; set; }

    public int ProfileReads { get; private set; }

    public int IpReads { get; private set; }

    public MemReadiness WithAttestation(bool allowed = true)
    {
        Attestations.Add(new(Account, Ip, allowed, Ist.At(26, 20, 0)));
        return this;
    }

    public Task<ReadinessCheckRecord?> LastCheckAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Checks.LastOrDefault(c => c.AccountId == accountId));

    public Task<ReadinessCheckRecord> AppendCheckAsync(ReadinessCheckDraft draft, CancellationToken cancellationToken)
    {
        var record = new ReadinessCheckRecord(Checks.Count + 1, draft.At, draft.AccountId, draft.SessionDate, draft.Verdict, draft.Reasons);
        Checks.Add(record);
        return Task.FromResult(record);
    }

    public Task<bool> HasDayStateAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken) => Task.FromResult(Days.Any(d => d.AccountId == accountId && d.SessionDate == session));

    public Task<bool> AppendDayStateAsync(DayStateDraft draft, CancellationToken cancellationToken)
    {
        if (Days.Any(d => d.AccountId == draft.AccountId && d.SessionDate == draft.SessionDate))
        {
            return Task.FromResult(false);
        }

        Days.Add(draft);
        return Task.FromResult(true);
    }

    public Task<IpAttestation?> NewestAttestationAsync(Guid accountId, string ip, CancellationToken cancellationToken) => Task.FromResult(Attestations.LastOrDefault(a => a.AccountId == accountId && a.Ip == ip));

    public Task<IReadOnlyList<Guid>> AssistedAccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([.. Assisted]);

    public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken cancellationToken) => Task.FromResult(TradingDay);

    public Task<ConnectionFacts?> ReadAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Connection);

    ReadinessSettings? IReadinessSettings.Current() => Settings;

    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(CancellationToken cancellationToken)
    {
        ProfileReads++;
        return BrokerThrows ? throw new InvalidOperationException("boom") : Task.FromResult(Profile);
    }

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(CancellationToken cancellationToken)
    {
        IpReads++;
        return Task.FromResult(StaticIp);
    }
}

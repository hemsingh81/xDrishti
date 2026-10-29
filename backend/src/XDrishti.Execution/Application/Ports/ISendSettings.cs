namespace XDrishti.Execution.Application.Ports;

/// <summary>The settings the send protocol reads on every send (all from the execution settings snapshot, ranges enforced by the registry).</summary>
public sealed record SendSettings(int PerSecond, int PerMinute, int PerDay, TimeSpan OrderValidFor, TimeSpan SendTimeout, decimal ExitCollarPct, int TokenMinRemainingMinutes);

/// <summary>The current send settings, or null when they cannot be vouched for (never read, or the last good read is too old). Null makes every send a refusal: unavailable never counts as a pass.</summary>
public interface ISendSettings
{
    SendSettings? Current();
}

/// <summary>The reason a connection's token cannot be used now, or "open". The persistent implementation reads three columns of the stored connection.</summary>
public interface ITokenGate
{
    Task<Domain.GateDecision> CheckAsync(Guid accountId, int minRemainingMinutes, CancellationToken cancellationToken);
}

/// <summary>The times of the sends already begun (write-ahead rows) since a moment, so that the day and minute counts survive a restart. Throws when the history cannot be read; the sender then refuses.</summary>
public interface ISendHistory
{
    Task<IReadOnlyList<DateTimeOffset>> BeginsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

namespace XDrishti.Execution.Domain;

public enum ExecEventKind
{
    State,
    LegSent,
    LegAcked,
    LegUpdate,
    Fill,
    ModifySent,
    CancelSent,
    CheckRun,
    HaltSeen,
    Alarm,
}

public enum ExecEventSource
{
    Engine,
    BrokerUpdate,
    BrokerPoll,
    Owner,
    Recovery,
}

/// <summary>
/// One fact about an intent. <see cref="Seq"/> starts at 1 and continues without gaps. A <see cref="ExecEventKind.State"/> event carries From and To; a <see cref="ExecEventKind.Fill"/> event carries the filled quantity.
/// The payload is at most 4 KB of text and never holds a secret.
/// </summary>
public sealed record ExecEvent(
    int Seq,
    ExecEventKind Kind,
    ExecState? From,
    ExecState? To,
    ExecEventSource Source,
    string Actor,
    DateTimeOffset At,
    int FillQty = 0,
    string Payload = "{}")
{
    public const int MaxPayloadBytes = 4096;

    public static ExecEvent Move(int seq, ExecState from, ExecState to, ExecEventSource source, string actor, DateTimeOffset at) =>
        new(seq, ExecEventKind.State, from, to, source, actor, at);

    public static ExecEvent Fill(int seq, int qty, ExecEventSource source, string actor, DateTimeOffset at) =>
        new(seq, ExecEventKind.Fill, null, null, source, actor, at, qty);
}

using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Execution.Domain;

public enum ExecSide
{
    Buy,
    Sell,
}

public enum ExecStructure
{
    Super,
    PlainStop,
}

/// <summary>
/// The execution intent of one approved ticket: an immutable fold of its events. <see cref="Apply"/> returns the next intent or a refusal and never changes this one, so a refused move changes nothing and
/// the accepted events replay to the same state. Only the Domain core is here; storing, checking and sending are other slices.
/// </summary>
public sealed class ExecIntent
{
    private static readonly string[] SecretWords = ["token", "secret", "password", "apikey", "authorization"];

    private ExecIntent(ExecIntent from)
    {
        Id = from.Id;
        PublicId = from.PublicId;
        AccountId = from.AccountId;
        PlanItemId = from.PlanItemId;
        Side = from.Side;
        Symbol = from.Symbol;
        SecurityId = from.SecurityId;
        Qty = from.Qty;
        Entry = from.Entry;
        Stop = from.Stop;
        Target = from.Target;
        Structure = from.Structure;
        State = from.State;
        Seq = from.Seq;
        FilledQty = from.FilledQty;
    }

    private ExecIntent()
    {
    }

    public long Id { get; private init; }

    public Guid PublicId { get; private init; }

    public Guid AccountId { get; private init; }

    public long PlanItemId { get; private init; }

    public ExecSide Side { get; private init; }

    public string Symbol { get; private init; } = string.Empty;

    public string SecurityId { get; private init; } = string.Empty;

    public int Qty { get; private init; }

    public decimal Entry { get; private init; }

    public decimal Stop { get; private init; }

    public decimal Target { get; private init; }

    /// <summary>Always <c>INTRADAY</c> in P12.</summary>
    public static string Product => "INTRADAY";

    public ExecStructure Structure { get; private init; }

    public ExecState State { get; private set; } = ExecState.Planned;

    public int Seq { get; private set; }

    public int FilledQty { get; private set; }

    public static Result<ExecIntent> Create(long id, Guid publicId, Guid accountId, long planItemId, ExecSide side, string symbol, string securityId, int qty, decimal entry, decimal stop, decimal target, ExecStructure structure)
    {
        if (qty <= 0 || entry <= 0 || stop <= 0 || target <= 0 || string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(securityId))
        {
            return Result.Failure<ExecIntent>(Error.Validation("execution.intent_invalid", "An intent needs a symbol, a security id, a positive quantity and positive levels."));
        }

        return Result.Success(new ExecIntent
        {
            Id = id,
            PublicId = publicId,
            AccountId = accountId,
            PlanItemId = planItemId,
            Side = side,
            Symbol = symbol,
            SecurityId = securityId,
            Qty = qty,
            Entry = entry,
            Stop = stop,
            Target = target,
            Structure = structure,
        });
    }

    /// <summary>Applies the next event. The sequence must continue; a state event must start from the current state and be a legal move; a part-filled entry is never cancelled; a fill never exceeds the quantity.</summary>
    public Result<ExecIntent> Apply(ExecEvent e)
    {
        if (e.Seq != Seq + 1)
        {
            return Fail("execution.event_conflict", $"Event {e.Seq} does not continue {Seq}.");
        }

        if (Encoding.UTF8.GetByteCount(e.Payload) > ExecEvent.MaxPayloadBytes || SecretWords.Any(w => e.Payload.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("execution.event_conflict", "The payload is too large or names a secret.");
        }

        var next = new ExecIntent(this) { Seq = e.Seq };
        switch (e.Kind)
        {
            case ExecEventKind.State:
                if (e.From is not { } from || e.To is not { } to || from != State)
                {
                    return Fail("execution.event_conflict", $"A state event must start from {State}.");
                }

                var legal = ExecLifecycle.Check(from, to);
                if (legal.IsFailure)
                {
                    return Result.Failure<ExecIntent>(legal.Error);
                }

                if (from == ExecState.Open && to == ExecState.Cancelled && FilledQty > 0)
                {
                    return Fail(ExecLifecycle.IllegalMoveCode, "An entry with a fill is not cancelled; cancelling the rest of a part fill is open to filled.");
                }

                next.State = to;
                break;
            case ExecEventKind.Fill:
                if (State is not (ExecState.Open or ExecState.Filled or ExecState.Protected or ExecState.Managing or ExecState.Closing))
                {
                    return Fail("execution.event_conflict", $"A fill cannot occur in {State}.");
                }

                if (e.FillQty <= 0 || FilledQty + e.FillQty > Qty)
                {
                    return Fail("execution.event_conflict", "A fill is positive and never takes the filled quantity above the intent quantity.");
                }

                next.FilledQty = FilledQty + e.FillQty;
                break;
            default:
                break;
        }

        return Result.Success(next);
    }

    private static Result<ExecIntent> Fail(string code, string message) => Result.Failure<ExecIntent>(Error.Conflict(code, message));
}

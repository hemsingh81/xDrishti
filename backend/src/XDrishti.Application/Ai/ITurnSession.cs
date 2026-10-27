namespace XDrishti.Application.Ai;

/// <summary>
/// A turn that passed every refusal and holds the single turn slot. Dispose releases the slot. <see cref="RunAsync"/> runs the loop once, sending its events to the sink; it does not throw for a failed or cancelled turn (it stores it and says so in the result).
/// </summary>
public interface ITurnSession : IAsyncDisposable
{
    ValueTask<TurnResult> RunAsync(Func<TurnEvent, ValueTask> sink, CancellationToken cancellationToken);
}

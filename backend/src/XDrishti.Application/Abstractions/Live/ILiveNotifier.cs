namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The feed's signals to the API through PostgreSQL <c>LISTEN/NOTIFY</c> (ADR 0024 §6). Both carry no payload: the receiver reads what changed from the database. With no listener a notification is harmless.
/// The throttle (at most one <c>live_prices</c> a second) is the caller's.
/// </summary>
public interface ILiveNotifier
{
    /// <summary>Sends <c>NOTIFY live_prices</c>: live prices were written.</summary>
    ValueTask PricesChangedAsync(CancellationToken cancellationToken);

    /// <summary>Sends <c>NOTIFY active_set_changed</c>: a holding or a price alert changed, the feed should recompute its set now.</summary>
    ValueTask ActiveSetChangedAsync(CancellationToken cancellationToken);
}

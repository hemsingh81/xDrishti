namespace XDrishti.Application.Live.Push;

/// <summary>
/// What the hub knows of one screen: its session, the instruments it follows and the price last sent for each of them (so only differences are sent). The instruments and the memory change together, under one lock; the lock is never held across a send.
/// </summary>
internal sealed class PushConnection(string id, Guid sessionId)
{
    private readonly Lock _sync = new();
    private HashSet<Guid> _instruments = [];
    private readonly Dictionary<Guid, InstrumentPrice> _sent = [];

    public string Id { get; } = id;

    public Guid SessionId { get; } = sessionId;

    /// <summary>The instruments to read for this connection now.</summary>
    public IReadOnlyCollection<Guid> Following
    {
        get
        {
            lock (_sync)
            {
                return _instruments.Count == 0 ? [] : [.. _instruments];
            }
        }
    }

    /// <summary>
    /// Follows these instruments from now on. What was sent for an instrument that stays followed is kept (a client resubscribes with a longer list as it pages, and already has those prices); an instrument that is new gets its current
    /// price in the next message, and one that is dropped is forgotten.
    /// </summary>
    public void Follow(IEnumerable<Guid> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        lock (_sync)
        {
            _instruments = [.. instruments];
            foreach (var id in _sent.Keys.Where(id => !_instruments.Contains(id)).ToList())
            {
                _sent.Remove(id);
            }
        }
    }

    public void Unfollow() => Follow([]);

    /// <summary>
    /// The prices among <paramref name="current"/> that this connection follows and has not been sent in this form: different from the last one sent and not older (by <see cref="InstrumentPrice.PriceAt"/>) than it. Nothing is
    /// remembered here; the caller reports what was actually delivered with <see cref="MarkSent"/>, so a failed send is offered again at the next push.
    /// </summary>
    public List<InstrumentPrice> TakeChanged(IReadOnlyDictionary<Guid, InstrumentPrice> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var changed = new List<InstrumentPrice>();
        lock (_sync)
        {
            foreach (var id in _instruments)
            {
                if (current.TryGetValue(id, out var price) && IsNewerThanSent(id, price))
                {
                    changed.Add(price);
                }
            }
        }

        changed.Sort(static (a, b) => a.InstrumentId.CompareTo(b.InstrumentId));
        return changed;
    }

    /// <summary>Remembers that these prices reached the client. Prices of instruments no longer followed, or older than what is remembered, are ignored.</summary>
    public void MarkSent(IEnumerable<InstrumentPrice> delivered)
    {
        ArgumentNullException.ThrowIfNull(delivered);
        lock (_sync)
        {
            foreach (var price in delivered)
            {
                if (_instruments.Contains(price.InstrumentId) && IsNewerThanSent(price.InstrumentId, price))
                {
                    _sent[price.InstrumentId] = price;
                }
            }
        }
    }

    private bool IsNewerThanSent(Guid id, InstrumentPrice price) =>
        !_sent.TryGetValue(id, out var last) || (price != last && price.PriceAt >= last.PriceAt);
}

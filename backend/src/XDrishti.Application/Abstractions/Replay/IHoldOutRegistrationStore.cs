namespace XDrishti.Application.Abstractions.Replay;

/// <summary>A stored pre-registration of the system replay (SR-03 D1): the canonical config text and its SHA-256, insert-only. <c>Mode</c> is <c>owner</c> or <c>auto</c>.</summary>
public sealed record StoredHoldOutRegistration(Guid Id, DateOnly BlockStart, string ConfigJson, string ConfigHash, string Mode, string Actor, DateTimeOffset CreatedAt);

/// <summary>The one result of a registration (SR-03 D4), insert-only: the verdict word, the figures of both arms as JSON, the engine versions and who ran it.</summary>
public sealed record StoredHoldOutResult(Guid RegistrationId, Guid UnlockId, IReadOnlyList<Guid> RunIds, string Verdict, string FiguresJson, string EngineVersions, string Mode, string Actor, DateTimeOffset At);

/// <summary>
/// The registrations and results of the system replay over a hold-out block (the registration and result tables of the research schema, SR-03), behind a port. Both are insert-only for the runtime role. The results are research only: no evidence reader
/// reads them (an architecture test pins that only the store and the SR-03 code name the tables).
/// </summary>
public interface IHoldOutRegistrationStore
{
    /// <summary>Stores a registration.</summary>
    ValueTask InsertAsync(StoredHoldOutRegistration registration, CancellationToken cancellationToken);

    /// <summary>One registration, or null.</summary>
    ValueTask<StoredHoldOutRegistration?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The registrations, newest first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The registrations that target the block, newest first.</summary>
    ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ForBlockAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>Stores the result; false when the registration has one already.</summary>
    ValueTask<bool> InsertResultAsync(StoredHoldOutResult result, CancellationToken cancellationToken);

    /// <summary>The result of a registration, or null.</summary>
    ValueTask<StoredHoldOutResult?> GetResultAsync(Guid registrationId, CancellationToken cancellationToken);
}

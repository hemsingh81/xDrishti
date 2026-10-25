using XDrishti.Domain.Audit;

namespace XDrishti.Application.Abstractions.Audit;

/// <summary>
/// One insert-only store behind the audit screen (spec P8-22, ADR 0117). A reader selects named columns only, newest first by <c>(at, id)</c>, bounded by the window and <c>Take</c>; it never reads a secret column and writes nothing. A new store registers one more reader.
/// </summary>
public interface IAuditSource
{
    AuditSource Source { get; }

    /// <summary>The next rows after the place in <paramref name="read"/>, newest first, at most <see cref="AuditRead.Take"/>.</summary>
    ValueTask<IReadOnlyList<AuditRaw>> ReadAsync(AuditRead read, CancellationToken cancellationToken);

    /// <summary>One row with its detail fields, or null when the id is not a row.</summary>
    ValueTask<AuditRaw?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>The rows of the window per stored kind.</summary>
    ValueTask<IReadOnlyList<AuditKindCount>> CountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Where a read continues: rows older than <paramref name="At"/>; with <paramref name="SameAt"/> also rows at that moment, and with <paramref name="BeforeId"/> only those whose id sorts before it.</summary>
public sealed record AuditAfter(DateTimeOffset At, bool SameAt, string? BeforeId);

/// <summary>A read of one store. <paramref name="Kinds"/> null = every kind; <paramref name="AccountId"/> only for the account store.</summary>
public sealed record AuditRead(DateTimeOffset From, DateTimeOffset To, AuditAfter? After, IReadOnlyList<string>? Kinds, Guid? AccountId, int Take);

/// <summary>A labelled plain-text value of the detail of a row.</summary>
public sealed record AuditField(string Label, string Value);

/// <summary>A row as stored: the text is raw and is redacted by the handler before it goes anywhere. <paramref name="Fields"/> is empty in a list read.</summary>
public sealed record AuditRaw(AuditSource Source, string Id, DateTimeOffset At, string Kind, string? Actor, Guid? AccountId, string? Text, long? PlanId, long? ItemId, IReadOnlyList<AuditField> Fields);

/// <summary>Rows of one stored kind in a window.</summary>
public sealed record AuditKindCount(string Kind, int Count);

/// <summary>The names of accounts, read at display time.</summary>
public interface IAuditAccountNames
{
    ValueTask<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

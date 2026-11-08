using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>Recomputes the chain of the shadow record from the stored rows (spec CF-08 criterion 4, <c>xd paper shadow verify</c>). Read only; it also reads the digest log outside the database and checks it against the chain.</summary>
public sealed record VerifyShadowChainQuery : IQuery<ShadowChainCheckView>;

/// <summary>The answer of a chain check.</summary>
/// <param name="Sessions">Stored sessions, ok and gap.</param>
/// <param name="Rows">Stored rows.</param>
/// <param name="Intact">Whether every row hash and every digest recomputes from the stored columns.</param>
/// <param name="FirstBroken">The first session (in date order) that does not match; everything after it is unproven. Null when intact.</param>
/// <param name="LastSession">The newest stored session, or null.</param>
/// <param name="LastDigest">Its stored digest: the value to keep outside the database.</param>
/// <param name="Gaps">Of the stored sessions, how many are gap sessions.</param>
/// <param name="SessionDigests">The stored digest of every session (the log outside the database is checked against them).</param>
/// <param name="LogProblem">Why the digest log outside the database disagrees with the chain or cannot be trusted; null when it agrees (or none was ever written).</param>
public sealed record ShadowChainCheckView(int Sessions, int Rows, bool Intact, DateOnly? FirstBroken, DateOnly? LastSession, string? LastDigest, int Gaps = 0, IReadOnlyDictionary<DateOnly, string>? SessionDigests = null, string? LogProblem = null);

/// <summary>Recomputes every row hash and digest from the stored columns and names the first session that does not match (<see cref="ShadowDigest.FirstBroken"/>). It reads the chain through the store's read member and writes nothing.</summary>
internal sealed class VerifyShadowChainHandler(IShadowStore store, IShadowDigestSink sink, IServiceRunStore runs) : IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView>
{
    public async ValueTask<ShadowChainCheckView> HandleAsync(VerifyShadowChainQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var chain = await store.ReadChainAsync(cancellationToken);
        var broken = ShadowDigest.FirstBroken(chain);
        var last = chain.OrderBy(s => s.SessionDate).LastOrDefault();
        var digests = chain.ToDictionary(s => s.SessionDate, s => s.Digest);
        var log = await sink.ReadAsync(cancellationToken);
        var earlier = (await runs.ListAsync(ShadowDigestService.Id, 0, 100, cancellationToken)).Any(r => r.Status == ServiceRunStatus.Succeeded && (r.Message?.StartsWith(ShadowDigestExported.ExportedPrefix, StringComparison.Ordinal) ?? false));
        return new ShadowChainCheckView(chain.Count, chain.Sum(s => s.Entries.Count), broken is null, broken, last?.SessionDate, last?.Digest, chain.Count(s => s.Status == "gap"), digests, ShadowDigestLogCheck.Problem(log, digests, earlier));
    }
}

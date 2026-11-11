using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.DerivedBaskets;

/// <summary>
/// The derived baskets' versions (BK-03). A version, its scores and the dated member changes are written in ONE transaction: a failure leaves no partial version and the previous membership stays served. A member row is dated like every other
/// (<c>valid_from</c> = the start of the India date the version takes effect), so <c>members(asOf)</c> works unchanged. A newer version for the same effective date supersedes the older one: the older version's rows for it are closed with zero length and the older version stays readable.
/// </summary>
internal sealed class DerivedVersionStore(XDrishtiDbContext db, IAccountEventLog events) : IDerivedVersionStore
{
    public async ValueTask<IReadOnlyList<DerivedDefinitionRow>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from d in db.DerivedDefinitions.AsNoTracking()
            join b in db.Baskets.AsNoTracking() on d.BasketId equals b.Id
            join s in db.BasketParamSets.AsNoTracking() on d.ParamsSetId equals s.Id into sets
            from s in sets.DefaultIfEmpty()
            orderby d.DefinitionNo
            select new { d.BasketId, b.Name, d.DefinitionNo, d.Enabled, d.ParamsSetId, Params = s == null ? null : s.Params }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new DerivedDefinitionRow(r.BasketId, r.Name, r.DefinitionNo, r.Enabled, r.ParamsSetId, ParseOverrides(r.Params)))];
    }

    public async ValueTask<PreviousVersion?> PreviousAsync(Guid basketId, DateOnly asOf, CancellationToken cancellationToken, string provenance = BasketProvenance.Derived)
    {
        var version = await db.BasketVersions.AsNoTracking()
            .Where(v => v.BasketId == basketId && v.Provenance == provenance && v.Outcome == VersionOutcomes.Applied && v.AsOf < asOf)
            .OrderByDescending(v => v.AsOf).ThenByDescending(v => v.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return null;
        }

        var scores = await db.BasketMemberScores.AsNoTracking().Where(s => s.VersionId == version.Id).ToListAsync(cancellationToken);
        return new PreviousVersion(version.Id, version.AsOf, version.EffectiveFrom, [.. scores.Select(s => new VersionScore(s.InstrumentId, s.Score, s.Rank, s.IsMember, s.SessionsHeld))]);
    }

    public async ValueTask<(DateOnly? FirstEffectiveFrom, DateOnly? NewestAsOf)> PublishedRangeAsync(Guid basketId, CancellationToken cancellationToken)
    {
        var published = db.BasketVersions.AsNoTracking().Where(v => v.BasketId == basketId && v.Provenance == BasketProvenance.Derived);
        return (await published.MinAsync(v => (DateOnly?)v.EffectiveFrom, cancellationToken), await published.MaxAsync(v => (DateOnly?)v.AsOf, cancellationToken));
    }

    public async ValueTask<bool> HasOtherRecomputedAsync(Guid basketId, DateOnly from, DateOnly to, string paramsHash, CancellationToken cancellationToken) =>
        await db.BasketVersions.AsNoTracking().AnyAsync(v => v.BasketId == basketId && v.Provenance == BasketProvenance.Recomputed && v.AsOf >= from && v.AsOf <= to && v.ParamsHash != paramsHash, cancellationToken);

    public async ValueTask<bool> ExistsAsync(Guid basketId, DateOnly asOf, string paramsHash, CancellationToken cancellationToken) =>
        await db.BasketVersions.AsNoTracking().AnyAsync(v => v.BasketId == basketId && v.AsOf == asOf && v.ParamsHash == paramsHash && v.FormedAt == null, cancellationToken);

    public async ValueTask<bool> FrozenExistsAsync(Guid basketId, DateOnly asOf, CancellationToken cancellationToken) =>
        await db.BasketVersions.AsNoTracking().AnyAsync(v => v.BasketId == basketId && v.AsOf == asOf && v.FormedAt != null && v.Provenance == BasketProvenance.Derived, cancellationToken);

    public async ValueTask<DerivedWriteResult> WriteAsync(DerivedVersionWrite version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        // The context retries transient failures: the whole transaction is then one retriable unit.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(version, async (_, state, ct) => await WriteOnceAsync(state, ct), null, cancellationToken);
    }

    private async Task<DerivedWriteResult> WriteOnceAsync(DerivedVersionWrite version, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); // nothing of an earlier, failed attempt is kept
        var live = version.Provenance == BasketProvenance.Derived;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (live)
        {
            var newest = await db.BasketVersions.AsNoTracking().Where(v => v.BasketId == version.BasketId && v.Provenance == BasketProvenance.Derived).MaxAsync(v => (DateOnly?)v.AsOf, cancellationToken);
            if (newest > version.AsOf)
            {
                return new DerivedWriteResult(DerivedWriteResult.Behind, 0, 0);
            }
        }

        if (await ExistsAsync(version.BasketId, version.AsOf, version.ParamsHash, cancellationToken))
        {
            return new DerivedWriteResult(VersionOutcomes.Unchanged, 0, 0);
        }

        var memberIds = version.Scores.Where(s => s.IsMember).Select(s => s.InstrumentId).ToList();
        db.BasketVersions.Add(new BasketVersionRecord
        {
            Id = version.VersionId,
            BasketId = version.BasketId,
            AsOf = version.AsOf,
            EffectiveFrom = version.EffectiveFrom,
            FormedAt = version.FormedAt,
            Params = version.ParamsJson,
            ParamsHash = version.ParamsHash,
            UniverseSize = version.UniverseSize,
            MemberCount = memberIds.Count,
            BelowThreshold = version.BelowThreshold,
            Excluded = version.ExcludedJson,
            Outcome = version.Outcome,
            CreatedAt = version.CreatedAt,
            Provenance = version.Provenance,
        });
        db.BasketMemberScores.AddRange(version.Scores.Select(s => new BasketMemberScoreRecord
        {
            VersionId = version.VersionId,
            InstrumentId = s.InstrumentId,
            Score = s.Score,
            Rank = s.Rank,
            IsMember = s.IsMember,
            SessionsHeld = s.SessionsHeld,
        }));

        var from = version.FormedAt ?? Start(version.EffectiveFrom); // a frozen intraday version counts from its formation time, not from the start of the day (BK-03b: no look-ahead)
        var added = 0;
        var removed = 0;
        try
        {
            if (live)
            {
                var open = await db.BasketMembers.Where(m => m.BasketId == version.BasketId && m.ValidTo == null).ToListAsync(cancellationToken);
                foreach (var stay in open.Where(m => m.ValidFrom >= from))
                {
                    stay.Remove(version.Actor, stay.ValidFrom); // an older version's stay for this same effective date: closed with zero length
                }

                var present = open.Where(m => m.ValidFrom < from).ToList();
                var keep = memberIds.ToHashSet();
                foreach (var stay in present.Where(m => !keep.Contains(m.InstrumentId)))
                {
                    stay.Remove(version.Actor, from);
                    removed++;
                }

                await db.SaveChangesAsync(cancellationToken); // the closes first: the index of open stays allows one per instrument
                var presentIds = present.Select(m => m.InstrumentId).ToHashSet();
                for (var i = 0; i < memberIds.Count; i++)
                {
                    if (!presentIds.Contains(memberIds[i]))
                    {
                        db.BasketMembers.Add(BasketMember.Derived(version.BasketId, memberIds[i], i, from, null, version.CreatedAt, version.VersionId, version.Provenance, version.Actor));
                        added++;
                    }
                }

                if (added + removed > 0)
                {
                    (await db.Baskets.FirstAsync(b => b.Id == version.BasketId, cancellationToken)).Touch();
                    events.Record(AccountEvent.Record(
                        AccountEventType.BasketMembershipChanged, null, $"Derived basket '{version.BasketName}' as of {version.AsOf:yyyy-MM-dd}: {added} added, {removed} removed", version.Actor, version.CreatedAt));
                }
            }
            else
            {
                var to = version.ValidTo is { } end ? Start(end) : (DateTimeOffset?)null;
                db.BasketMembers.AddRange(memberIds.Select((id, i) => BasketMember.Derived(version.BasketId, id, i, from, to, version.CreatedAt, version.VersionId, version.Provenance, version.Actor)));
                added = memberIds.Count;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // a concurrent writer got the same version in first: nothing of this one is kept
            return new DerivedWriteResult(VersionOutcomes.Unchanged, 0, 0);
        }

        return new DerivedWriteResult(VersionOutcomes.Applied, added, removed);
    }

    public async ValueTask<BasketVersionPage?> ListVersionsAsync(Guid basketId, int skip, int take, CancellationToken cancellationToken)
    {
        var definitionNo = await db.DerivedDefinitions.AsNoTracking().Where(d => d.BasketId == basketId).Select(d => (int?)d.DefinitionNo).FirstOrDefaultAsync(cancellationToken);
        if (definitionNo is not { } no)
        {
            return null;
        }

        var query = db.BasketVersions.AsNoTracking().Where(v => v.BasketId == basketId);
        var rows = await query.OrderByDescending(v => v.AsOf).ThenByDescending(v => v.CreatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new BasketVersionPage([.. rows.Select(Summary)], await query.CountAsync(cancellationToken), DerivedScoreUnits.Info(no));
    }

    public async ValueTask<BasketVersionDetail?> GetVersionAsync(Guid basketId, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await db.BasketVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId && v.BasketId == basketId, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var scores = await (
            from s in db.BasketMemberScores.AsNoTracking()
            join i in db.Instruments.AsNoTracking() on s.InstrumentId equals i.Id
            where s.VersionId == versionId
            orderby s.IsMember descending, s.Rank, i.CanonicalSymbol
            select new { s.InstrumentId, i.CanonicalSymbol, i.Name, s.Score, s.Rank, s.IsMember }).ToListAsync(cancellationToken);
        var excluded = JsonSerializer.Deserialize<List<VersionExclusion>>(version.Excluded) ?? [];
        return new BasketVersionDetail(
            Summary(version),
            version.BelowThreshold,
            [.. scores.Select(s => new VersionScoreView(s.InstrumentId, Bare(s.CanonicalSymbol), s.Name, s.Score, s.Rank, s.IsMember))],
            [.. excluded.Select(e => new VersionExclusionView(e.InstrumentId, Bare(e.Symbol), e.Reason))],
            ParseValues(version.Params));
    }

    public async ValueTask<bool> SaveDefinitionAsync(Guid basketId, bool? enabled, NewParamSet? paramSet, string auditText, string actor, CancellationToken cancellationToken)
    {
        var definition = await db.DerivedDefinitions.FirstOrDefaultAsync(d => d.BasketId == basketId, cancellationToken);
        if (definition is null)
        {
            return false;
        }

        if (paramSet is not null)
        {
            db.BasketParamSets.Add(new BasketParamSetRecord
            {
                Id = paramSet.Id,
                DefinitionNo = paramSet.DefinitionNo,
                Params = paramSet.ParamsJson,
                ParamsHash = paramSet.ParamsHash,
                CreatedAt = paramSet.CreatedAt,
                CreatedBy = actor,
            });
            definition.ParamsSetId = paramSet.Id;
        }

        if (enabled is { } on)
        {
            definition.Enabled = on;
        }

        events.Record(AccountEvent.Record(AccountEventType.DerivedBasketChanged, null, auditText, actor, paramSet?.CreatedAt ?? DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static DateTimeOffset Start(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();

    private static string Bare(string symbol) => symbol.StartsWith("NSE:", StringComparison.Ordinal) ? symbol[4..] : symbol;

    private static BasketVersionSummary Summary(BasketVersionRecord v) => new(v.Id, v.AsOf, v.EffectiveFrom, v.FormedAt, v.MemberCount, v.UniverseSize, v.ParamsHash, v.Outcome, v.Provenance);

    private static Dictionary<string, double> ParseOverrides(string? json) => json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? [];

    private static Dictionary<string, double> ParseValues(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("values", out var values) ? JsonSerializer.Deserialize<Dictionary<string, double>>(values.GetRawText()) ?? [] : [];
    }
}

using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Research;

/// <summary>The strategy store port for the handler tests: _versions are put in with <see cref="Put"/> and read back; an append is not what the handler does, so it is not supported (the rules of the real store are tested against the database).</summary>
internal sealed class FakeStrategyStore : IStrategyStore
{
    private readonly List<StoredVersion> _versions = [];

    /// <summary>Stores a version (numbered by the caller) of a family.</summary>
    public StoredVersion Put(string id, int version, string sourceText, string? hash = null)
    {
        var stored = new StoredVersion(id, version, 1, hash ?? (id + version).PadRight(64, 'f')[..64], sourceText, "{}", null, null, DateTimeOffset.UnixEpoch);
        _versions.Add(stored);
        return stored;
    }

    public ValueTask<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredStrategy>>([.. _versions.GroupBy(v => v.StrategyId).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new StoredStrategy(g.Key, "intraday", false, DateTimeOffset.UnixEpoch, g.Max(v => v.Version), g.Count()))]);

    public async ValueTask<StoredStrategy?> GetAsync(string id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(s => s.Id == id);

    public ValueTask<StoredVersion?> GetVersionAsync(string id, int? version, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_versions.Where(v => v.StrategyId == id && (version is null || v.Version == version)).OrderByDescending(v => v.Version).FirstOrDefault());

    public ValueTask<StoredVersion?> GetByHashAsync(string hash, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_versions.FirstOrDefault(v => v.Hash == hash));

    public ValueTask<Result<AppendResult>> AppendAsync(NewVersion version, CancellationToken cancellationToken) =>
        throw new NotSupportedException("the handler reads the store; it does not write");
}

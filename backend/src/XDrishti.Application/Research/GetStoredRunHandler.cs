using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>Finds a stored run by key (spec P3-10, D7). A key that is not 64 lower-case hex is not found without asking the store.</summary>
internal sealed class GetStoredRunHandler(IRunStore store) : IQueryHandler<GetStoredRunQuery, Result<StoredRunHeader>>
{
    /// <summary>The code of a run that is not stored.</summary>
    public const string NotFoundCode = "store.run_not_found";

    public async ValueTask<Result<StoredRunHeader>> HandleAsync(GetStoredRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var header = RunRecordText.IsHex64(query.RunKey) ? await store.FindAsync(query.RunKey, cancellationToken) : null;
        return header is null
            ? Result.Failure<StoredRunHeader>(Error.NotFound(NotFoundCode, "No run with this key is stored."))
            : Result.Success(header);
    }
}

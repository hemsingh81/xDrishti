namespace XDrishti.Application.Abstractions.Messaging;

/// <summary>Marker for a read-only request that returns <typeparamref name="TResult"/>.</summary>
public interface IQuery<TResult>;

/// <summary>Handles one query. One handler per use case keeps classes small and independently testable.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    ValueTask<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

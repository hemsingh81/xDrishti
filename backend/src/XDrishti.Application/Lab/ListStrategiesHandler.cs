using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Application.Lab;

internal sealed class ListStrategiesHandler(IStrategyStore store) : IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>>
{
    public ValueTask<IReadOnlyList<StoredStrategy>> HandleAsync(ListStrategiesQuery query, CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
}

using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

internal sealed class GetStrategySchemaHandler(IStrategyChecker checker) : IQueryHandler<GetStrategySchemaQuery, string>
{
    public ValueTask<string> HandleAsync(GetStrategySchemaQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(checker.SchemaJson());
}

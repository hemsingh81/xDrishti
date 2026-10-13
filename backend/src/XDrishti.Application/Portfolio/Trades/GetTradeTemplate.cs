using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The template of a tradebook file (<see cref="TradeTemplate"/>): the canonical header and two example rows, as CSV text.</summary>
public sealed record GetTradeTemplateQuery : IQuery<string>;

internal sealed class GetTradeTemplateHandler : IQueryHandler<GetTradeTemplateQuery, string>
{
    public ValueTask<string> HandleAsync(GetTradeTemplateQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(TradeTemplate.Csv);
}

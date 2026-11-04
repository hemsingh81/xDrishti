using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary><c>research.expression_runs</c> and <c>research.expression_trades</c> behind a port (P14-08): insert-only and content-addressed; nothing is updated or deleted.</summary>
public interface IExpressionTradeStore
{
    /// <summary>Whether a run with the id is stored.</summary>
    ValueTask<bool> ExistsAsync(string runId, CancellationToken cancellationToken);

    /// <summary>Stores the header and every row in one transaction; false (nothing written) when the id is stored already. A row count that differs from <c>Trades</c> is refused.</summary>
    ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, CancellationToken cancellationToken);

    /// <summary>As above, with the legs of the paired structures (<c>research.expression_legs</c>, P14-10) in the same transaction. A leg of a trade that is not a row of the run, or of an unpaired row, is refused.</summary>
    ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, IReadOnlyList<StructureLegRow> legs, CancellationToken cancellationToken);
}

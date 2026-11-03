using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Pairs;

namespace XDrishti.Infrastructure.Pairs;

internal sealed class PairSettings(IOptions<PairsRunOptions> options) : IPairSettings
{
    public decimal PairNotional => options.Value.PairNotional!.Value;

    public int MaxCandidates => options.Value.MaxCandidates!.Value;

    public string? CostSheetId => string.IsNullOrWhiteSpace(options.Value.CostSheetId) ? null : options.Value.CostSheetId.Trim();
}

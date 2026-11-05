using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>The decay monitor of one strategy's forward series (spec CF-12 D1, part 12a, ADR 0218). Read only: derived on request from stored grades and the settings; nothing is stored, proposed or changed.</summary>
/// <param name="Strategy">The strategy id (no `|`).</param>
public sealed record GetDecayReportQuery(string? Strategy) : IQuery<Result<DecayReportView>>;

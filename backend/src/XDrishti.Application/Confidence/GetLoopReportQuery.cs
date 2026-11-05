using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>"Is the learning loop helping?" (spec CF-12 D4 to D7, part 12a, ADR 0218): the three arms, the learning curve, the reliability slope and the decision-quality lift. Read only and derived on request; a section whose input has no source says <c>unavailable</c> and why.</summary>
public sealed record GetLoopReportQuery : IQuery<Result<LoopReportView>>;

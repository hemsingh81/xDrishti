using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Reports the state of the announcement eval set: the sample, the labels (or that they are awaited) and the baseline (P15-07).</summary>
public sealed record ShowAnnouncementsQuery : IQuery<AnnouncementEvalReport>;

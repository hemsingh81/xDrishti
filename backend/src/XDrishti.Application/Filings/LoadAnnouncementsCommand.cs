using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Loads the announcement csv files of the filings inbox (P15-07). Local files and the filings tables only: no network, no broker call.</summary>
public sealed record LoadAnnouncementsCommand : ICommand;

using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Loads the board-meeting csv files of the filings inbox into the calendar (P15-04). Local files and the filings tables only: no network, no broker call.</summary>
public sealed record LoadCalendarCommand : ICommand;

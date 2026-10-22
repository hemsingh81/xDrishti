using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Stores a comparison in the result store (spec P4-05, D10): the header row that names the members and keeps the table and the diagnostics. The members are stored as runs of their own first (<see cref="StoreRunCommand"/>); a comparison whose
/// members are not stored is refused. The text and its digest come from the caller (the verb), who made them from the report it printed.
/// </summary>
/// <param name="Comparison">The comparison.</param>
/// <param name="Record">The text of the table and the diagnostics with their digest.</param>
/// <param name="Actor">Who stores it (<c>cli:simulate</c> for the verb).</param>
public sealed record StoreComparisonCommand(ComparisonRun? Comparison, ComparisonRecordText Record, string Actor) : ICommand;

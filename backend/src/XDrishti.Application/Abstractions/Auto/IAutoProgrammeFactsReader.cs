namespace XDrishti.Application.Abstractions.Auto;

/// <summary>The stored facts read from the ledger, the switch log, the plans and the ADR folder (spec AU-19). A null is a fact that could not be read, never zero.</summary>
public sealed record AutoStoredFacts(int? RecoveryPasses, int? MultiDayRecoveries, bool? RemovalAdrPresent, int? SwitchChanges, int? AutoOnNightsWithSuggestions);

/// <summary>The select-only reads of the AU programme check (spec AU-19). Writes nothing. The simulation job and its fence are read through the SimAll ports, not here.</summary>
public interface IAutoProgrammeFactsReader
{
    ValueTask<AutoStoredFacts> ReadAsync(CancellationToken cancellationToken);
}

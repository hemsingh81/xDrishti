namespace XDrishti.Simulation;

/// <summary>The instrument a simulation runs on: its id and the symbol shown in results.</summary>
/// <param name="Id">The instrument id.</param>
/// <param name="Symbol">The symbol.</param>
public readonly record struct InstrumentRef(Guid Id, string Symbol);

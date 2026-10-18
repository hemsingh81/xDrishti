namespace XDrishti.Simulation;

/// <summary>The instrument a simulation runs on: its id and its symbol, as a trade carries them.</summary>
public readonly record struct InstrumentRef(Guid Id, string Symbol);

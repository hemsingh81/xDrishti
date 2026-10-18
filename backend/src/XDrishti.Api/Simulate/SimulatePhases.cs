namespace XDrishti.Api.Simulate;

/// <summary>
/// The wall time of the phases the verb itself can observe (spec P3-04b2, D7), from <see cref="TimeProvider"/>: resolving the universe and the tick gate, the run of the handler (validate, prepare, read, replay and assembly; the handler logs
/// those four apart in its finish line), and rendering and writing the files. Printed and logged, never stored in a file. Timing is a data-quality fact, not a trading number.
/// </summary>
internal sealed record SimulatePhases(TimeSpan ResolveAndGate, TimeSpan Run, TimeSpan RenderAndWrite);

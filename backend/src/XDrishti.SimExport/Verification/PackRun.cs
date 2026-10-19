using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Verification;

/// <summary>A run of the pack with its labels (<see cref="RunLabels.Of"/>), so that the renderers of this library need no fill rules.</summary>
public sealed record PackRun(SimulationRun Run, RunLabelSet Labels);

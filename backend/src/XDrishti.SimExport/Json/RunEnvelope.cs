using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Json;

/// <summary>The top level of <c>run.json</c>, in the order the file has: the export header, the labels, the run (spec P3-04b1, E4).</summary>
internal sealed record RunEnvelope(ExportHeader Export, LabelsView Labels, SimulationRun Run);

/// <summary>The format string the caller gave and the schema of this envelope (1).</summary>
internal sealed record ExportHeader(string Format, int Schema);

/// <summary>The labels as the file has them: the lines, what the fills do not model, and the flag that says the oracle comparison has been shown for this engine version (<see cref="RunLabels.IsVerified"/>; it says nothing about charts).</summary>
internal sealed record LabelsView(IReadOnlyList<string> Lines, IReadOnlyList<FillLimit> NotModelled, bool EngineVerified);

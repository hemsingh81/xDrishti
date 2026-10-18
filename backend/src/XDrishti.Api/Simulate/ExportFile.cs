using System.Buffers;

namespace XDrishti.Api.Simulate;

/// <summary>A file of an export: its name relative to the run folder (<see cref="XDrishti.SimExport.Manifest.ExportNames"/>) and the function that renders its bytes into the sink the writer hands it.</summary>
internal sealed record ExportFile(string Name, Action<IBufferWriter<byte>> Render);

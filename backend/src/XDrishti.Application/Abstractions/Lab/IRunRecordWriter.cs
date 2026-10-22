using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// The canonical record text of a run and the digests of its two trade files, exactly as the <c>simulate</c> verb makes them (ADR 0041, 0045). A port because the renderers live in the export library, which only the host may name; the result is
/// the same bytes, so a Lab run and a verb run of the same input are the same stored run.
/// </summary>
public interface IRunRecordWriter
{
    /// <summary>Renders the record without trades and the digests of <c>trades.csv</c> and <c>open_at_end.csv</c>.</summary>
    RunRecordText Write(SimulationRun run);
}

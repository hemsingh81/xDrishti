using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Writes a hypothesis down (P6-10, ADR 0076): one knob, from the owner or from the grid, with a stored baseline run. Nothing is run or approved. A parameter knob must name a parameter of the baseline run at its stored value; a grid hypothesis must
/// carry a value on the deterministic grid around it.
/// </summary>
/// <param name="Source">Owner or Grid.</param>
/// <param name="CellKey">The cell it is about (<c>strategy|tf|side|style|exit|regime</c>), or null.</param>
/// <param name="Knob">The one knob with its baseline and candidate values.</param>
/// <param name="Rationale">Why, 1 to 500 characters.</param>
/// <param name="BaselineRunKey">The stored baseline run.</param>
/// <param name="Grid">The grid rules (read for a grid hypothesis).</param>
/// <param name="Actor">Who proposes.</param>
public sealed record ProposeHypothesisCommand(HypothesisSource Source, string? CellKey, ExperimentKnob Knob, string Rationale, string BaselineRunKey, HypothesisGrid Grid, string Actor) : ICommand;

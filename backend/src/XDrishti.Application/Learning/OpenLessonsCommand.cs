using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Opens lessons from the monitors that can run on replayed history (P6-16, ADR 0078): growing loss tags (P6-15) and the drift alarms still on at the end of the replay (P6-09). The same finding never opens a second lesson. With
/// <paramref name="DryRun"/> nothing is written.
/// </summary>
/// <param name="Sources">One to 50 stored runs, one run per strategy, each with the style and exit label of its cell.</param>
/// <param name="Drift">The drift knobs.</param>
/// <param name="LossTags">The loss-tag knobs.</param>
/// <param name="MonitorMonths">The monitored period of the drift replay (1 to 120).</param>
/// <param name="ModelId">A registered model whose calibration to watch, or null.</param>
/// <param name="DryRun">List the findings and say which are already lessons; write nothing.</param>
/// <param name="Actor">Who runs it.</param>
public sealed record OpenLessonsCommand(IReadOnlyList<CellSource> Sources, DriftOptions Drift, LossTagOptions LossTags, int MonitorMonths, long? ModelId, bool DryRun, string Actor) : ICommand;

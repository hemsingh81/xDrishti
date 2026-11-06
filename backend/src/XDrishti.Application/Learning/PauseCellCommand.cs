using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Pauses a cell (<c>Paper</c> or <c>Active</c> to <c>Paused</c>) through the P6-08 lifecycle (spec CF-12 part 12b, ADR 0219). This is the only move the automatic path can ask for: it names no other target state and no re-enable. A pause lowers risk, needs no fact
/// and needs evidence.
/// </summary>
/// <param name="Cell">The cell key.</param>
/// <param name="Reason">Why, at most 400 characters.</param>
/// <param name="Actor">Who or what asks: <c>owner:...</c> or <c>auto</c>.</param>
/// <param name="EvidenceIds">What justifies it (at least one).</param>
/// <param name="BeforeRef">A reference to the thing before (the setting row of an automatic decision), or null.</param>
/// <param name="AfterRef">A reference to the thing after (the settings hash), or null.</param>
public sealed record PauseCellCommand(string Cell, string Reason, string Actor, IReadOnlyList<string> EvidenceIds, string? BeforeRef = null, string? AfterRef = null) : ICommand;

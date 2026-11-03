namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>The settings <c>Pairs:*</c> (P14-13): the Y-leg notional in rupees, the candidate cap and the owner's charge sheet id (empty: gross, labelled).</summary>
public interface IPairSettings
{
    decimal PairNotional { get; }

    int MaxCandidates { get; }

    string? CostSheetId { get; }
}

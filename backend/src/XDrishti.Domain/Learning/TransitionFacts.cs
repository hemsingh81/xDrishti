namespace XDrishti.Domain.Learning;

/// <summary>What the caller states to justify a cell transition (spec P6-08). They are recorded with the row (evidence ids, paper days); no component re-derives them yet, so they are claims with evidence ids, not proofs.</summary>
/// <param name="GatesPassed">The out-of-sample qualification gates passed (Research to Candidate).</param>
/// <param name="OwnerApproved">The owner approved the step (Candidate to Paper, Paper to Active).</param>
/// <param name="WithinBand">The paper results stayed within the backtest band (Paper to Active).</param>
/// <param name="Recovered">The paused cell recovered in shadow (Paused to Paper).</param>
/// <param name="PaperDays">Days followed on paper (Paper to Active).</param>
public sealed record TransitionFacts(bool GatesPassed = false, bool OwnerApproved = false, bool WithinBand = false, bool Recovered = false, int PaperDays = 0);

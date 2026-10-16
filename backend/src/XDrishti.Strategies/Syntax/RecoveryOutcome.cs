namespace XDrishti.Strategies.Syntax;

/// <summary>What the recovery did, besides the tree and the problems: how many times it parsed the tokens (<see cref="Attempts"/>, never more than <see cref="Recovery.MaxAttempts"/>, which is what bounds the work) and whether it gave up before the end of the text (<see cref="Truncated"/>: there are more problems than were reported).</summary>
internal readonly record struct RecoveryOutcome(int Attempts, bool Truncated);

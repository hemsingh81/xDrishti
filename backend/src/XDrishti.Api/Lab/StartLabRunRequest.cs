namespace XDrishti.Api.Lab;

/// <summary>The universe of a posted run: exactly one of a basket and a list of symbols.</summary>
internal sealed record StartLabRunUniverse(Guid? BasketId = null, IReadOnlyList<string>? Symbols = null);

/// <summary>The window of a posted run, India dates, inclusive; both or neither.</summary>
internal sealed record StartLabRunWindow(DateOnly From, DateOnly To);

/// <summary>Body of <c>POST /api/lab/runs</c> (spec P5-02): a stored strategy version (the latest when <c>version</c> is omitted), the universe, the method (<c>quick</c>) and optionally the window (default: the last year of stored data), <c>slippage</c> (<c>default</c> or <c>conservative</c>: tiered floors, tier C excluded; CF-05) and <c>stress</c> (the cost stress block; default on for <c>robust</c>, off for <c>quick</c> and <c>full</c>), <c>nulls</c> (<c>false</c> leaves the chance and plateau block out of a <c>robust</c> run; default true) and <c>draws</c> (random-entry seeds, 200 to <c>Confidence:Nulls:MaxDraws</c>, default the configured 1000); either makes the run a labelled what-if (CF-07, ADR 0231).</summary>
internal sealed record StartLabRunRequest(string? StrategyId = null, int? Version = null, StartLabRunUniverse? Universe = null, string? Method = null, StartLabRunWindow? Window = null, string? Slippage = null, bool? Stress = null, bool? Nulls = null, int? Draws = null);

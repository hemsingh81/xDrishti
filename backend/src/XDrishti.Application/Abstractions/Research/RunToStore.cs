using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>What the store is given (spec P3-10, D7): the run, its record text with digests, who stores it and the instant and the purpose (<c>research</c> unless a training job says otherwise; ADR 0243; read once by the handler from <c>TimeProvider</c>; the record reads no clock).</summary>
public sealed record RunToStore(SimulationRun Run, RunRecordText Record, string Actor, DateTimeOffset CreatedAt, RunPurpose Purpose = RunPurpose.Research);

namespace XDrishti.Api.Simulate;

/// <summary>What the process was started to do (spec P3-04b2, D2): serve the API, or one of the one-shot steps.</summary>
internal enum Verb
{
    /// <summary>No verb: run the API.</summary>
    Serve,

    /// <summary>The <c>simulate</c> verb: first argument <c>simulate</c>, nothing else selects it.</summary>
    Simulate,

    /// <summary>The dropped <c>cost-sheet</c> verb (spec P3-03f, ADR 0292): first argument <c>cost-sheet</c>, nothing else selects it; it only refuses, so its arguments never select another step.</summary>
    CostSheet,

    /// <summary>The <c>verify-pack</c> verb (spec P3-09): first argument <c>verify-pack</c>, nothing else selects it.</summary>
    VerifyPack,

    /// <summary>The <c>xd</c> verb (spec P5-07): first argument <c>xd</c>, nothing else selects it.</summary>
    Xd,

    /// <summary>The container health probe (<c>--healthcheck</c>).</summary>
    HealthCheck,

    /// <summary>The deployment step <c>migrate</c>.</summary>
    Migrate,

    /// <summary>The deployment step <c>owner-reset</c>.</summary>
    OwnerReset,
}

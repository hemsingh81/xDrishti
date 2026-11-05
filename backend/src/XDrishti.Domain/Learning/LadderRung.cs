namespace XDrishti.Domain.Learning;

/// <summary>The rungs of the complexity ladder (CF-10 part 10b, decision D2): a rung may be used only if it beats the rung below.</summary>
public enum LadderRung
{
    /// <summary>The base rate.</summary>
    BaseRate = 1,

    /// <summary>The shrunk cell rate.</summary>
    CellRate = 2,

    /// <summary>L2 logistic regression with few features.</summary>
    Logistic = 3,

    /// <summary>Gradient-boosted trees (monotone constraints, depth at most 3).</summary>
    Trees = 4,
}

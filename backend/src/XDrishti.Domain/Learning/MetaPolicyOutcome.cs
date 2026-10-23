namespace XDrishti.Domain.Learning;

/// <summary>What the meta-labelling evaluation concludes (P6-13). "Does not help" is an ordinary answer.</summary>
public enum MetaPolicyOutcome
{
    /// <summary>There is no champion model (or it is not the model evaluated): no policy.</summary>
    NoPolicy,

    /// <summary>Fewer evaluable folds than needed to say anything.</summary>
    NotEnoughEvidence,

    /// <summary>Taking everything is as good or better out of sample.</summary>
    DoesNotHelp,

    /// <summary>It looks helpful but the evidence is too thin or a doc 09 section 14 flag is raised.</summary>
    Inconclusive,

    /// <summary>It beats taking everything out of sample in enough folds and pooled.</summary>
    Helps,
}

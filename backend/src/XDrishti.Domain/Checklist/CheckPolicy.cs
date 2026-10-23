namespace XDrishti.Domain.Checklist;

/// <summary>What a failed check means for approval (doc 16 <c>pretrade_checks</c>). An unavailable check always needs confirmation, whatever its policy.</summary>
public enum CheckPolicy
{
    /// <summary>Shown on the ticket; approval needs nothing more.</summary>
    Warn,

    /// <summary>Approval needs the owner's confirmation with a reason.</summary>
    BlockWithConfirm,
}

namespace XDrishti.Application.Reporting;

/// <summary>Approve decisions of one kind (P8-04).</summary>
/// <param name="Kind">owner or auto; an automatic approval is never counted as the owner's.</param>
/// <param name="Count">Approvals.</param>
public sealed record ApprovalCountView(string Kind, int Count);

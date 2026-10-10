using XDrishti.Domain.Auth;

namespace XDrishti.Application.Abstractions.Auth;

/// <summary>Append-only authentication audit trail. New entries are saved by <see cref="IAuthUnitOfWork"/>.</summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent);

    /// <summary>Newest first.</summary>
    ValueTask<IReadOnlyList<AuditEvent>> ListLatestAsync(int take, CancellationToken cancellationToken);
}

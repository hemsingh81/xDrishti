using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Infrastructure.Execution;

namespace XDrishti.Infrastructure;

/// <summary>Steps of the one-shot <c>xd-api migrate</c> that follow the migrations and need the secret files (wiring, so that the host names no assistant type).</summary>
public static class PostMigration
{
    /// <summary>Gives the assistant's read-only roles their login when both password files exist (spec P9-02 AC 13); the roles stay NOLOGIN otherwise.</summary>
    public static Task<bool> ProvisionReadOnlyRolesAsync(IServiceProvider services, CancellationToken cancellationToken = default) => AiRoleProvisioner.ProvisionAsync(services, cancellationToken);

    /// <summary>Gives the execution role its login when the password file exists (spec P12-01b); the role stays NOLOGIN otherwise.</summary>
    public static Task<bool> ProvisionExecutionRoleAsync(IServiceProvider services, CancellationToken cancellationToken = default) => ExecRoleProvisioner.ProvisionAsync(services, cancellationToken);
}

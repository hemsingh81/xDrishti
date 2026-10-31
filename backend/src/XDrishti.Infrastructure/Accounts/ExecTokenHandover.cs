using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// The execution token hand-over in <c>accounts.exec_token_handover</c> (ADR 0171). Writes are tracked by the scoped context and saved by the accounts unit of work
/// together with the vault row and the connection. The token is sealed with the hand-over key (purpose <see cref="SecretCipherPurpose.ExecHandover"/>), bound to the
/// account and the name <c>access_token</c>. Nothing is logged.
/// </summary>
internal sealed class ExecTokenHandover(XDrishtiDbContext db, [FromKeyedServices(SecretCipherPurpose.ExecHandover)] BrokerSecretCipher cipher) : IExecTokenHandover
{
    /// <summary>The name the seal is bound to (part of the associated data).</summary>
    public static readonly string Name = BrokerSecretNames.Key(BrokerSecretName.AccessToken);

    public async ValueTask SetAsync(Guid accountId, SecretValue accessToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sealedToken = cipher.Encrypt(accountId, Name, accessToken);
        var record = await db.ExecTokenHandovers.FindAsync([accountId], cancellationToken);
        if (record is null)
        {
            db.ExecTokenHandovers.Add(new ExecTokenHandoverRecord(accountId, sealedToken, BrokerSecretCipher.CurrentKeyVersion, now));
        }
        else
        {
            record.Replace(sealedToken, BrokerSecretCipher.CurrentKeyVersion, now);
        }
    }

    public async ValueTask DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var record = await db.ExecTokenHandovers.FindAsync([accountId], cancellationToken);
        if (record is not null)
        {
            db.ExecTokenHandovers.Remove(record);
        }
    }
}

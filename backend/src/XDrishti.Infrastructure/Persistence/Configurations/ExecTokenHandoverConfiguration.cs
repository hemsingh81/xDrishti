using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class ExecTokenHandoverConfiguration : IEntityTypeConfiguration<ExecTokenHandoverRecord>
{
    public void Configure(EntityTypeBuilder<ExecTokenHandoverRecord> builder)
    {
        builder.ToTable("exec_token_handover", Schemas.Accounts);
        builder.HasKey(x => x.AccountId);
        builder.Property(x => x.AccountId).ValueGeneratedNever();
        builder.Property(x => x.SealedToken).IsRequired();
        builder.Property(x => x.KeyVersion).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // The hand-over goes with its account, like the vault rows.
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

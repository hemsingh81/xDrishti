using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>accounts.report_client_codes</c> (ADR 0029): one row per account with the hash of the client code (UCC) that its imported reports carry - never the code itself. Remembered by the first report imported with a code; immutable and never deleted: the runtime role can read and insert only.
/// </summary>
internal sealed class ReportClientCodeConfiguration : IEntityTypeConfiguration<ReportClientCode>
{
    public void Configure(EntityTypeBuilder<ReportClientCode> builder)
    {
        builder.ToTable("report_client_codes", Schemas.Accounts, t => t.HasCheckConstraint("ck_report_client_codes_hash", "length(hash) = 64"));
        builder.HasKey(x => x.AccountId);
        builder.Property(x => x.AccountId).ValueGeneratedNever();
        builder.HasOne<BrokerAccount>().WithOne().HasForeignKey<ReportClientCode>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Hash).HasMaxLength(ReportClientCode.HashLength).IsRequired();
        builder.Property(x => x.RememberedAt).IsRequired();
    }
}

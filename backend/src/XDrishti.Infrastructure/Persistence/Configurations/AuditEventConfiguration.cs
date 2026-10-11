using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Auth;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", Schemas.Auth);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.At).IsRequired();
        builder.HasIndex(x => x.At);
        builder.Property(x => x.Event).HasColumnName("event").HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(AuditEvent.MaxEmailLength);
        builder.Property(x => x.Ip).HasMaxLength(AuditEvent.MaxIpLength);
        builder.Property(x => x.UserAgent).HasMaxLength(AuditEvent.MaxUserAgentLength);
        builder.Property(x => x.Detail).HasMaxLength(AuditEvent.MaxDetailLength);
    }
}

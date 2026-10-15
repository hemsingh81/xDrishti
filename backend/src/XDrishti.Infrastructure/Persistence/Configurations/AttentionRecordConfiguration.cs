using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.attention_items</c> (ADR 0027): the attention items of the bell as they were last seen - id (which includes the situation), kind, severity, title, text, actions (JSON), when first seen, when the condition stopped holding and the account they are about.
/// Written with set-based upserts by one read of the notices; never deleted (the runtime role has no DELETE), a resolved item is simply not returned after a day.
/// </summary>
internal sealed class AttentionRecordConfiguration : IEntityTypeConfiguration<AttentionRecord>
{
    public void Configure(EntityTypeBuilder<AttentionRecord> builder)
    {
        builder.ToTable("attention_items", Schemas.Portfolio, t => t.HasCheckConstraint("ck_attention_items_severity", "severity IN ('error', 'warning', 'info')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(AttentionRecord.MaxIdLength).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(AttentionRecord.MaxTitleLength).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(AttentionRecord.MaxTextLength).IsRequired();
        builder.Property(x => x.Actions).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Since).IsRequired();
        builder.Property(x => x.ResolvedAt);
        builder.Property(x => x.AccountId);
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.ResolvedAt).HasDatabaseName("ix_attention_items_resolved");
    }
}

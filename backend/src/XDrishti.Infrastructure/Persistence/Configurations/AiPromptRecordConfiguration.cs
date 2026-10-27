using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.prompts</c> (spec P9-03): the prompt text once per hash.</summary>
internal sealed class AiPromptRecordConfiguration : IEntityTypeConfiguration<AiPromptRecord>
{
    public void Configure(EntityTypeBuilder<AiPromptRecord> builder)
    {
        builder.ToTable("prompts", Schemas.Ai);
        builder.HasKey(x => x.Hash).HasName("pk_prompts");
        builder.Property(x => x.Hash).HasColumnType("character(64)");
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Text).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
    }
}

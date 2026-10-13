using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Platform;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>platform.app_settings</c>: the owner's settings as key/value rows (ADR 0002); the module that owns a key checks its values. Never deleted: a change overwrites the value.</summary>
internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings", Schemas.Platform);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("key").HasMaxLength(AppSetting.MaxKeyLength).ValueGeneratedNever();
        builder.Property(x => x.Value).HasMaxLength(AppSetting.MaxValueLength).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(AppSetting.MaxUpdatedByLength).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}

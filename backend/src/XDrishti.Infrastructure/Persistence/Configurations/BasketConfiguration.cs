using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    private static readonly ValueConverter<BasketPurpose, string> PurposeCodes = new(
        v => v.ToCode(), v => ParsePurpose(v));

    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.ToTable("baskets", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_baskets_purpose", "purpose IN ('trading', 'long_term', 'watch')");
            t.HasCheckConstraint("ck_baskets_colour", "colour ~ '^#[0-9a-f]{6}$'");
            t.HasCheckConstraint("ck_baskets_position", "position >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        // Names of active baskets are unique ignoring case: the index ux_baskets_active_name_lower in the migration (EF Core cannot model expression indexes).
        builder.Property(x => x.Name).HasMaxLength(BasketLimits.MaxNameLength).IsRequired();
        builder.Property(x => x.Colour).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Purpose).HasConversion(PurposeCodes).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(BasketLimits.MaxDescriptionLength);
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ArchivedAt);

        // Every change of a basket or of its members raises the revision; saving with an old one fails, so concurrent edits never overwrite each other.
        builder.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        builder.Ignore(x => x.IsArchived);
    }

    private static BasketPurpose ParsePurpose(string code) =>
        BasketPurposeCodes.TryParse(code, out var purpose) ? purpose : throw new InvalidOperationException($"Unknown basket purpose '{code}' in the database.");
}

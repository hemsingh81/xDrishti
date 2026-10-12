using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BasketMemberConfiguration : IEntityTypeConfiguration<BasketMember>
{
    public void Configure(EntityTypeBuilder<BasketMember> builder)
    {
        builder.ToTable("basket_members", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_basket_members_dates", "valid_to IS NULL OR valid_to >= valid_from");
            t.HasCheckConstraint("ck_basket_members_removed_by", "(valid_to IS NULL) = (removed_by IS NULL)");
            t.HasCheckConstraint("ck_basket_members_position", "position >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.ValidFrom).IsRequired();
        builder.Property(x => x.ValidTo);
        builder.Property(x => x.Note).HasMaxLength(BasketLimits.MaxNoteLength);
        builder.Property(x => x.AddedBy).HasMaxLength(BasketMember.MaxActorLength).IsRequired();
        builder.Property(x => x.RemovedBy).HasMaxLength(BasketMember.MaxActorLength);
        builder.Ignore(x => x.IsActive);

        // Baskets and instruments are never deleted (baskets are archived, instruments retired), so the history is always complete.
        builder.HasOne<Basket>().WithMany().HasForeignKey(x => x.BasketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        // An instrument is in a basket once at a time, however many baskets it is in; removing ends the stay, adding again makes a new row.
        builder.HasIndex(x => new { x.BasketId, x.InstrumentId }).IsUnique().HasDatabaseName("ux_basket_members_active").HasFilter("valid_to IS NULL");
    }
}

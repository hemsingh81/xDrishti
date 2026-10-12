using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class TrackedInstrumentConfiguration : IEntityTypeConfiguration<TrackedInstrument>
{
    public void Configure(EntityTypeBuilder<TrackedInstrument> builder)
    {
        builder.ToTable("tracked_instruments", Schemas.Market, t =>
        {
            // Active and deactivated_at always agree; the history range is never backwards.
            t.HasCheckConstraint("ck_tracked_instruments_state", "(active AND deactivated_at IS NULL) OR (NOT active AND deactivated_at IS NOT NULL)");
            t.HasCheckConstraint("ck_tracked_instruments_data_range", "data_from IS NULL OR data_to IS NULL OR data_from <= data_to");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("instrument_id").ValueGeneratedNever();

        // Tracking rows are kept for good, so an instrument with one can never be deleted from the master list (the importer only retires).
        builder.HasOne<InstrumentRecord>().WithOne().HasForeignKey<TrackedInstrument>(x => x.Id).OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency: PostgreSQL's system column xmin changes with every update of the row, so a save based on a row that was changed
        // (or untracked) since it was read matches no row and is refused. No column of our own is needed.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(x => x.Active).IsRequired();
        builder.Property(x => x.ActiveSince).IsRequired();
        builder.Property(x => x.DeactivatedAt);
        builder.Property(x => x.DataFrom);
        builder.Property(x => x.DataTo);
        builder.Property(x => x.LastUpdate);
        builder.Property(x => x.BackfillRequestedAt);
        builder.Property(x => x.Note).HasMaxLength(TrackedInstrument.MaxNoteLength);
        builder.Property(x => x.TrackedBy).HasMaxLength(TrackedInstrument.MaxTrackedByLength).IsRequired();

        // "How many / which are tracked now" is asked on every list and facet call.
        builder.HasIndex(x => x.Active).HasDatabaseName("ix_tracked_instruments_active");
    }
}

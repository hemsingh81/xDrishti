using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class InstrumentBrokerIdConfiguration : IEntityTypeConfiguration<InstrumentBrokerIdRecord>
{
    public void Configure(EntityTypeBuilder<InstrumentBrokerIdRecord> builder)
    {
        builder.ToTable("instrument_broker_ids", Schemas.Market);

        // An instrument has one id per broker; a broker id (within its exchange segment) belongs to one instrument.
        builder.HasKey(x => new { x.InstrumentId, x.Broker });
        builder.Property(x => x.Broker).HasMaxLength(16).IsRequired();
        builder.Property(x => x.BrokerId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Segment).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.Broker, x.BrokerId, x.Segment }).IsUnique();
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

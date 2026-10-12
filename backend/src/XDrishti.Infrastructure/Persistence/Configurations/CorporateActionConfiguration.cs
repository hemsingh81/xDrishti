using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.corporate_actions</c> (ADR 0017 §5): a split or bonus of an instrument on its ex-date, unique per instrument, ex-date and kind. The ratio is <c>numerator</c>:<c>denominator</c>, the price
/// <c>factor</c> for the bars before the ex-date is derived from it and stored. A suggestion has no decision; every other status has one. Inserted and updated, never deleted: a declined action is
/// <c>ignored</c> (the runtime role has no DELETE).
/// </summary>
internal sealed class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        builder.ToTable("corporate_actions", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_corporate_actions_kind", "kind IN ('split', 'bonus')");
            t.HasCheckConstraint("ck_corporate_actions_status", "status IN ('suggested', 'applied', 'ignored')");
            t.HasCheckConstraint("ck_corporate_actions_source", "source IN ('manual', 'detected', 'csv')");
            t.HasCheckConstraint("ck_corporate_actions_ratio", "numerator > 0 AND denominator > 0 AND factor > 0");
            t.HasCheckConstraint(
                "ck_corporate_actions_decision",
                "(status = 'suggested' AND decided_at IS NULL AND decided_by IS NULL) OR (status <> 'suggested' AND decided_at IS NOT NULL AND decided_by IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExDate).IsRequired();
        builder.Property(x => x.Kind)
            .HasConversion(new ValueConverter<CorporateActionKind, string>(v => v.ToCode(), v => Parse(v)))
            .HasMaxLength(5).IsRequired();
        builder.Property(x => x.Factor).HasColumnType("numeric(24,12)").IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(EnumConversions.Lowercase<CorporateActionStatus>())
            .HasMaxLength(9).IsRequired();
        builder.Property(x => x.Source)
            .HasConversion(EnumConversions.Lowercase<CorporateActionSource>())
            .HasMaxLength(8).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(CorporateAction.MaxNoteLength);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.DecidedBy).HasMaxLength(256);
        builder.Property(x => x.DetectedExDate);
        builder.Ignore(x => x.Ratio);
        builder.HasOne<Market.InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.InstrumentId, x.ExDate, x.Kind }).IsUnique().HasDatabaseName("ux_corporate_actions_instrument_ex_date_kind");

        // "The suggestions waiting" (the alert on the Data screens) and the list by status, newest ex-date first.
        builder.HasIndex(x => new { x.Status, x.ExDate }).HasDatabaseName("ix_corporate_actions_status_ex_date");
    }

    private static CorporateActionKind Parse(string code) =>
        CorporateActionKindCodes.TryParse(code, out var kind) ? kind : throw new InvalidOperationException($"Unknown corporate action kind '{code}'.");
}

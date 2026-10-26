using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Reporting;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>workflow.charge_sheet_events</c> (spec P8-23, ADR 0127): every owner act on the charge-sheet confirmation (capability on or off, confirm, revoke), appended and never edited. The newest confirm-or-revoke row is what stands; the newest capability row says whether
/// confirming is allowed (no row: off). The shape is a check: an owner actor, a reason, and the sheet and rates hash on a confirmation or revoke only.
/// </summary>
internal sealed class ChargeSheetEventConfiguration : IEntityTypeConfiguration<ChargeSheetEventRecord>
{
    public void Configure(EntityTypeBuilder<ChargeSheetEventRecord> builder)
    {
        builder.ToTable("charge_sheet_events", Schemas.Workflow, t => t.HasCheckConstraint(
            "ck_charge_sheet_events_shape",
            "kind IN ('gate_on', 'gate_off', 'confirm', 'revoke') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0 "
            + "AND ((kind IN ('gate_on', 'gate_off') AND sheet_id IS NULL AND rates_sha256 IS NULL AND confirmed_on IS NULL) "
            + "OR (kind = 'confirm' AND sheet_id ~ '^[a-z0-9][a-z0-9._-]{0,39}$' AND sheet_id NOT LIKE 'test-%' AND rates_sha256 ~ '^[0-9a-f]{64}$' AND confirmed_on IS NOT NULL) "
            + "OR (kind = 'revoke' AND sheet_id ~ '^[a-z0-9][a-z0-9._-]{0,39}$' AND rates_sha256 ~ '^[0-9a-f]{64}$' AND confirmed_on IS NULL))"));
        builder.HasKey(x => x.Id).HasName("pk_charge_sheet_events");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Kind).HasMaxLength(10).IsRequired();
        builder.Property(x => x.SheetId).HasMaxLength(40);
        builder.Property(x => x.RatesSha256).HasMaxLength(64);
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.Id }).HasDatabaseName("ix_charge_sheet_events_kind_id");
    }
}

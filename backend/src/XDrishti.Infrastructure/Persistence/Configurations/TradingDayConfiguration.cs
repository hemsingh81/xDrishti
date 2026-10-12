using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.trading_days</c> (ADR 0017 §1): one row per date that is not an ordinary weekday session. No rows are seeded. A holiday has no session window, a special
/// session has one that starts before it ends. A confirmed row can carry a proposal of the stored bars next to it (<c>proposal_status</c> and the <c>proposed_*</c> columns). Optimistic
/// concurrency by xmin: a save based on a row someone changed meanwhile (a confirm while the observed pass withdraws its proposal) is refused instead of overwriting or deleting it.
/// </summary>
internal sealed class TradingDayConfiguration : IEntityTypeConfiguration<TradingDay>
{
    public void Configure(EntityTypeBuilder<TradingDay> builder)
    {
        builder.ToTable("trading_days", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_trading_days_kind", "kind IN ('holiday', 'special')");
            t.HasCheckConstraint("ck_trading_days_source", "source IN ('manual', 'observed', 'nse')");
            t.HasCheckConstraint("ck_trading_days_status", "status IN ('confirmed', 'proposed', 'dismissed')");
            t.HasCheckConstraint(
                "ck_trading_days_session",
                "(kind = 'holiday' AND session_from IS NULL AND session_to IS NULL) OR (kind = 'special' AND session_from IS NOT NULL AND session_to IS NOT NULL AND session_from < session_to)");
            t.HasCheckConstraint("ck_trading_days_proposal_status", "proposal_status IS NULL OR proposal_status IN ('proposed', 'dismissed')");
            t.HasCheckConstraint(
                "ck_trading_days_proposal",
                "(proposal_status IS NULL AND proposed_kind IS NULL AND proposed_from IS NULL AND proposed_to IS NULL AND proposed_note IS NULL)"
                + " OR (proposal_status = 'dismissed' AND proposed_kind IS NULL AND proposed_from IS NULL AND proposed_to IS NULL AND proposed_note IS NULL)"
                + " OR (proposal_status = 'proposed' AND ((proposed_kind = 'holiday' AND proposed_from IS NULL AND proposed_to IS NULL)"
                + " OR (proposed_kind = 'special' AND proposed_from IS NOT NULL AND proposed_to IS NOT NULL AND proposed_from < proposed_to)))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("date").ValueGeneratedNever();
        builder.Property(x => x.Kind)
            .HasConversion(new ValueConverter<CalendarDayKind, string>(v => v.ToCode(), v => ParseKind(v)))
            .HasMaxLength(7).IsRequired();
        builder.Property(x => x.SessionFrom).HasColumnType("time without time zone");
        builder.Property(x => x.SessionTo).HasColumnType("time without time zone");
        builder.Property(x => x.Source)
            .HasConversion(new ValueConverter<CalendarSource, string>(v => v.ToCode(), v => Enum.Parse<CalendarSource>(v, true)))
            .HasMaxLength(8).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(new ValueConverter<CalendarStatus, string>(v => v.ToCode(), v => Enum.Parse<CalendarStatus>(v, true)))
            .HasMaxLength(9).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(TradingDay.MaxNoteLength);
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ProposalStatus)
            .HasConversion(new ValueConverter<CalendarStatus?, string?>(v => v == null ? null : v.Value.ToCode(), v => v == null ? null : Enum.Parse<CalendarStatus>(v, true)))
            .HasMaxLength(9);
        builder.Property(x => x.ProposedKind)
            .HasConversion(new ValueConverter<CalendarDayKind?, string?>(v => v == null ? null : v.Value.ToCode(), v => v == null ? null : ParseKind(v)))
            .HasMaxLength(7);
        builder.Property(x => x.ProposedFrom).HasColumnType("time without time zone");
        builder.Property(x => x.ProposedTo).HasColumnType("time without time zone");
        builder.Property(x => x.ProposedNote).HasMaxLength(TradingDay.MaxNoteLength);

        // PostgreSQL's system column xmin changes with every update of the row, so an update or delete based on a stale row matches nothing and is refused (never deleting a row the owner just confirmed).
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }

    private static CalendarDayKind ParseKind(string code) =>
        CalendarDayKindCodes.TryParse(code, out var kind) ? kind : throw new InvalidOperationException($"Unknown calendar day kind '{code}'.");
}

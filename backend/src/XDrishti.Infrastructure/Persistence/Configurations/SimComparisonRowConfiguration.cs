using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.sim_comparisons</c> (spec P4-05, D10): one row per stored comparison of exits on the same entries. The comparison key is unique and is the idempotency key; the entries key is indexed, because two runs with the same entries key
/// are the ones that join on <c>trade_key</c>. The members are three parallel arrays of the same length (the run key, the candidate id and the gated count), in the order of the candidates. Insert-only for the runtime role.
/// </summary>
internal sealed class SimComparisonRowConfiguration : IEntityTypeConfiguration<SimComparisonRow>
{
    public void Configure(EntityTypeBuilder<SimComparisonRow> builder)
    {
        builder.ToTable("sim_comparisons", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_sim_comparisons_keys", "comparison_key ~ '^[0-9a-f]{64}$' AND entries_key ~ '^[0-9a-f]{64}$' AND record_sha256 ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_sim_comparisons_unit", "unit IN ('RFilled', 'RNet')");
            t.HasCheckConstraint("ck_sim_comparisons_members", "cardinality(member_run_keys) >= 2 AND cardinality(member_run_keys) = cardinality(member_candidate_ids) AND cardinality(member_run_keys) = cardinality(member_gated_blocked)");
            t.HasCheckConstraint("ck_sim_comparisons_counts", "common_entries >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_sim_comparisons");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.ComparisonKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.EntriesKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CommonEntries).IsRequired();
        builder.Property(x => x.MemberRunKeys).HasColumnType("character(64)[]").IsRequired();
        builder.Property(x => x.MemberCandidateIds).HasColumnType("character varying(200)[]").IsRequired();
        builder.Property(x => x.MemberGatedBlocked).HasColumnType("bigint[]").IsRequired();
        builder.Property(x => x.ComparisonCsv).HasColumnType("text").IsRequired();
        builder.Property(x => x.DiagnosticsCsv).HasColumnType("text").IsRequired();
        builder.Property(x => x.RecordSha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.ComparisonKey).IsUnique().HasDatabaseName("uq_sim_comparisons_comparison_key");
        builder.HasIndex(x => x.EntriesKey).HasDatabaseName("ix_sim_comparisons_entries_key");
    }
}

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Audit;

/// <summary><c>auth.audit_events</c>: sign-ins and security events. The address typed is the actor (the handler shows it only when it is the owner's); the IP address and user agent are selected by the detail read only.</summary>
internal sealed class AuthAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Auth;

    protected override string Table => "auth.audit_events";

    protected override string TimeColumn => "at";

    protected override string KindExpression => "event";

    protected override string ListColumns => "id, at, event, email, NULL::uuid, NULL::bigint, NULL::bigint, detail";

    protected override string DetailColumns => ", ip, user_agent";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader) =>
    [
        .. new[] { new AuditField("IP address", Text(reader, 8) ?? string.Empty), new AuditField("User agent", Text(reader, 9) ?? string.Empty) }.Where(f => f.Value.Length > 0),
    ];
}

/// <summary><c>accounts.account_events</c>: configuration, data and service actions.</summary>
internal sealed class AccountAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Account;

    protected override string Table => "accounts.account_events";

    protected override string TimeColumn => "at";

    protected override string KindExpression => "event";

    protected override string ListColumns => "id, at, event, actor, account_id, NULL::bigint, NULL::bigint, detail";

    protected override bool HasAccount => true;
}

/// <summary><c>workflow.approvals</c>: the decisions on tickets, by the owner or by the autonomy rule. The raw modification text and the settings hash are never selected; the detail adds the cutoff, the confirm reason, a sentence for the modification and the policy version.</summary>
internal sealed class ApprovalAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Approval;

    protected override string Table => "workflow.approvals";

    protected override string TimeColumn => "decided_at";

    protected override string KindExpression => "'approval.' || decision";

    protected override string ListColumns => "id, decided_at, 'approval.' || decision, actor, NULL::uuid, plan_id, item_id, reason";

    protected override string DetailColumns => ", cutoff, confirm_reason, modifications, policy_version, kind";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader)
    {
        var fields = new List<AuditField> { new("Cutoff", IstText(reader.GetFieldValue<DateTimeOffset>(8))), new("Decision by", Text(reader, 12) == "auto" ? "autonomy rule" : "owner") };
        if (Text(reader, 9) is { Length: > 0 } confirm)
        {
            fields.Add(new AuditField("Confirm reason", confirm));
        }

        if (Text(reader, 10) is { } modifications)
        {
            fields.Add(new AuditField("Modification", ModificationSentence(modifications)));
        }

        if (Text(reader, 11) is { Length: > 0 } policy)
        {
            fields.Add(new AuditField("Policy version", policy));
        }

        return fields;
    }

    /// <summary>One sentence from the stored quantity changes; the raw text is not shown.</summary>
    internal static string ModificationSentence(string stored)
    {
        var parsed = TicketModification.Parse(stored);
        if (parsed.IsFailure || parsed.Value.Count == 0)
        {
            return "Quantities were changed.";
        }

        var changes = parsed.Value.Select(c => $"account {c.AccountId.ToString("D")[..8]} to {c.Quantity.ToString(CultureInfo.InvariantCulture)}");
        return "Reduced the quantity for " + string.Join(", ", changes) + ".";
    }
}

/// <summary><c>trading.plan_events</c>: plan and ticket state moves.</summary>
internal sealed class PlanAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Plan;

    protected override string Table => "trading.plan_events";

    protected override string TimeColumn => "created_at";

    protected override string KindExpression => "CASE WHEN item_id IS NULL THEN 'plan.moved' ELSE 'plan.ticket_moved' END";

    protected override string ListColumns => $"id, created_at, {KindExpression}, actor, NULL::uuid, plan_id, item_id, from_state || ' to ' || to_state || ': ' || reason";

    protected override string DetailColumns => ", approval_id";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader) =>
        reader.IsDBNull(8) ? [] : [new AuditField("Approval", "approval:" + reader.GetInt64(8).ToString(CultureInfo.InvariantCulture))];
}

/// <summary><c>workflow.autonomy_settings</c>: the owner's changes of the autonomy switches. The snapshot hash is never selected.</summary>
internal sealed class AutonomyAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Autonomy;

    protected override string Table => "workflow.autonomy_settings";

    protected override string TimeColumn => "changed_at";

    protected override string KindExpression => "'autonomy.changed'";

    protected override string ListColumns =>
        "id, changed_at, 'autonomy.changed', actor, NULL::uuid, NULL::bigint, NULL::bigint, "
        + "'auto-approve ' || CASE WHEN auto_approve_tickets THEN 'on' ELSE 'off' END || ', reserve promotion ' || CASE WHEN promote_from_reserve THEN 'on' ELSE 'off' END || ', min win ' || min_p_win::text || ', max ' || max_auto_per_day::text || ' a day: ' || reason";

    protected override string DetailColumns => ", auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader) =>
    [
        new("Auto-approve tickets", reader.GetBoolean(8) ? "on" : "off"),
        new("Promote from reserve", reader.GetBoolean(9) ? "on" : "off"),
        new("Minimum win probability", reader.GetDecimal(10).ToString("0.###", CultureInfo.InvariantCulture)),
        new("Most automatic approvals a day", reader.GetInt32(11).ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary><c>research.learning_changelog</c>: cell moves, model designations and reverts.</summary>
internal sealed class LearningAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.Learning;

    protected override string Table => "research.learning_changelog";

    protected override string TimeColumn => "created_at";

    protected override string KindExpression => "'learning.' || kind";

    protected override string ListColumns => "id, created_at, 'learning.' || kind, actor, NULL::uuid, NULL::bigint, NULL::bigint, subject_kind || ' ' || subject || ': ' || from_state || ' to ' || to_state || ' - ' || reason";

    protected override string DetailColumns => ", subject, from_state, to_state, before_ref, after_ref, evidence_ids";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader)
    {
        var fields = new List<AuditField> { new("Subject", reader.GetString(8)), new("From to", reader.GetString(9) + " to " + reader.GetString(10)) };
        if (Text(reader, 11) is { Length: > 0 } before)
        {
            fields.Add(new AuditField("Before", before));
        }

        if (Text(reader, 12) is { Length: > 0 } after)
        {
            fields.Add(new AuditField("After", after));
        }

        var evidence = reader.IsDBNull(13) ? [] : reader.GetFieldValue<string[]>(13);
        if (evidence.Length > 0)
        {
            fields.Add(new AuditField("Evidence ids", string.Join(", ", evidence)));
        }

        return fields;
    }
}

/// <summary><c>workflow.charge_sheet_events</c> (P8-23): the owner's confirmations and revocations of the charge sheet and the switch that allows confirming. The detail adds the sheet and the first 12 characters of the rates hash.</summary>
internal sealed class ChargeSheetAuditSource(NpgsqlDataSource dataSource) : SqlAuditSource(dataSource)
{
    public override AuditSource Source => AuditSource.ChargeSheet;

    protected override string Table => "workflow.charge_sheet_events";

    protected override string TimeColumn => "created_at";

    protected override string KindExpression => "'chargesheet.' || kind";

    protected override string ListColumns => "id, created_at, 'chargesheet.' || kind, actor, NULL::uuid, NULL::bigint, NULL::bigint, COALESCE(sheet_id || ': ', '') || reason";

    protected override string DetailColumns => ", sheet_id, left(rates_sha256, 12)";

    protected override IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader) =>
    [
        .. new[] { new AuditField("Charge sheet", Text(reader, 8) ?? string.Empty), new AuditField("Rates (first 12 of the SHA-256)", Text(reader, 9) ?? string.Empty) }.Where(f => f.Value.Length > 0),
    ];
}

/// <summary>The names of accounts (<c>accounts.broker_accounts</c>: id and name only).</summary>
internal sealed class AuditAccountNames(NpgsqlDataSource dataSource) : IAuditAccountNames
{
    public async ValueTask<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var names = new Dictionary<Guid, string>();
        if (ids.Count == 0)
        {
            return names;
        }

        await using var command = dataSource.CreateCommand("SELECT id, name FROM accounts.broker_accounts WHERE id = ANY(@ids)");
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names[reader.GetGuid(0)] = reader.GetString(1);
        }

        return names;
    }
}

/// <summary>Registers the readers of the audit stores.</summary>
internal static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAuditSources(this IServiceCollection services)
    {
        services.AddScoped<IAuditSource, AuthAuditSource>();
        services.AddScoped<IAuditSource, AccountAuditSource>();
        services.AddScoped<IAuditSource, ApprovalAuditSource>();
        services.AddScoped<IAuditSource, PlanAuditSource>();
        services.AddScoped<IAuditSource, AutonomyAuditSource>();
        services.AddScoped<IAuditSource, LearningAuditSource>();
        services.AddScoped<IAuditSource, ChargeSheetAuditSource>();
        services.AddScoped<IAuditAccountNames, AuditAccountNames>();
        return services;
    }
}

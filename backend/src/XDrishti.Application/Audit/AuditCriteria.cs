using System.Globalization;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Audit;

/// <summary>The validated filters of a request (spec P8-22). Built only by <see cref="Parse"/>; an unknown value is refused naming the field, never ignored.</summary>
internal sealed class AuditCriteria
{
    public const int MinTextLength = 3;

    public const int MaxTextLength = 64;

    private AuditCriteria()
    {
    }

    public AuditWindow Window { get; private init; }

    /// <summary>The stores worth reading for these filters (a kind, category, outcome or account that a store cannot hold excludes it).</summary>
    public IReadOnlyList<AuditSource> Sources { get; private init; } = [];

    /// <summary>Per source, the stored kinds to ask for; absent = every kind.</summary>
    public IReadOnlyDictionary<AuditSource, IReadOnlyList<string>> Kinds { get; private init; } = new Dictionary<AuditSource, IReadOnlyList<string>>();

    public IReadOnlyCollection<string>? Actors { get; private init; }

    public Guid? AccountId { get; private init; }

    public string? Text { get; private init; }

    /// <summary>True when a part of the filter can only be applied after redaction in the application (actor kind, search text), so the stores are read in chunks.</summary>
    public bool NeedsScan => Actors is not null || Text is not null;

    /// <summary>True when any filter beyond the dates is set.</summary>
    public bool HasFilters { get; private init; }

    public static Result<AuditCriteria> Parse(AuditFilters filters, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var problems = new List<FieldError>();
        var window = ParseWindow(filters.From, filters.To, now, problems);

        var sources = List(filters.Source, "source", s => AuditSources.TryParse(s, out _), problems);
        var categories = List(filters.Category, "category", c => AuditKinds.Categories.Contains(c), problems);
        var outcomes = List(filters.Outcome, "outcome", o => AuditKinds.Outcomes.Contains(o), problems);
        var actors = List(filters.Actor, "actor", a => AuditActors.Filterable.Contains(a), problems);
        var kinds = List(filters.Kind, "kind", k => AuditSources.All.Any(s => AuditKinds.KindsOf(s).Any(x => x.Kind == k)), problems);

        Guid? account = null;
        if (!string.IsNullOrWhiteSpace(filters.Account))
        {
            if (Guid.TryParseExact(filters.Account.Trim(), "D", out var id))
            {
                account = id;
            }
            else
            {
                problems.Add(new FieldError("account", "The account is an account id."));
            }
        }

        string? text = null;
        if (!string.IsNullOrWhiteSpace(filters.Text))
        {
            text = filters.Text.Trim();
            if (text.Length is < MinTextLength or > MaxTextLength)
            {
                problems.Add(new FieldError("q", $"The search text is {MinTextLength} to {MaxTextLength} characters."));
            }
        }

        if (problems.Count > 0)
        {
            return Result.Failure<AuditCriteria>(AuditErrors.Invalid(problems));
        }

        var chosen = sources is null ? [.. AuditSources.All] : sources.Select(s => AuditSources.TryParse(s, out var parsed) ? parsed : default).Distinct().Order().ToList();
        var perSource = new Dictionary<AuditSource, IReadOnlyList<string>>();
        var read = new List<AuditSource>();
        foreach (var source in chosen)
        {
            if (account is not null && source != AuditSource.Account)
            {
                continue;
            }

            if (categories is null && outcomes is null && kinds is null)
            {
                read.Add(source);
                continue;
            }

            var matching = AuditKinds.KindsMatching(source, categories, outcomes, kinds);
            if (matching.Count > 0)
            {
                read.Add(source);
                perSource[source] = matching;
            }
        }

        return Result.Success(new AuditCriteria
        {
            Window = window,
            Sources = read,
            Kinds = perSource,
            Actors = actors,
            AccountId = account,
            Text = text,
            HasFilters = sources is not null || categories is not null || outcomes is not null || actors is not null || kinds is not null || account is not null || text is not null,
        });
    }

    public static AuditWindow ParseWindow(string? from, string? to, DateTimeOffset now, List<FieldError> problems)
    {
        var end = ParseDay(to, "to", IndiaTime.Today(now), problems);
        var start = ParseDay(from, "from", end.AddDays(1 - AuditWindow.DefaultDays), problems);
        if (problems.Count == 0 && AuditWindow.Problem(start, end) is { } problem)
        {
            problems.Add(new FieldError("from", problem));
        }

        return new AuditWindow(start, end);
    }

    private static DateOnly ParseDay(string? text, string field, DateOnly fallback, List<FieldError> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        problems.Add(new FieldError(field, "The date is yyyy-MM-dd."));
        return fallback;
    }

    private static List<string>? List(string? text, string field, Func<string, bool> known, List<FieldError> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var values = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        if (values.Count is 0 or > 20 || values.Any(v => !known(v)))
        {
            problems.Add(new FieldError(field, "An unknown value."));
            return null;
        }

        return values;
    }
}

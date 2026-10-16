using System.Diagnostics;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Records the SQL a request makes, to show how many reads a use case does.</summary>
internal static class StatementRecorder
{
    /// <summary>
    /// The text of every SQL statement the driver ran for <paramref name="request"/> and only for it: the request runs under a fresh trace of its own (sent as a W3C `traceparent` header to the in-process server), and the driver's command
    /// activities of that trace are collected — statements of background services or of other tests have another trace and are ignored.
    /// </summary>
    /// <param name="client">The client the request is sent with.</param>
    /// <param name="request">The request(s) to watch.</param>
    /// <param name="readsOnly">True (the default): only statements that read (contain SELECT) are collected. False: every statement, so a test can prove that a request wrote nothing.</param>
    public static async Task<List<string>> StatementsDuringAsync(this HttpClient client, Func<Task> request, bool readsOnly = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        const string TestSource = "xd-test-statements";
        var statements = new List<string>();
        var trace = default(ActivityTraceId);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Npgsql" or TestSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId != trace || activity.Source.Name != "Npgsql")
                {
                    return;
                }

                var text = readsOnly
                    ? activity.Tags.Select(t => t.Value).FirstOrDefault(v => v is not null && v.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                    : activity.Tags.Where(t => t.Key is "db.statement" or "db.query.text").Select(t => t.Value).FirstOrDefault();
                if (text is not null)
                {
                    lock (statements)
                    {
                        statements.Add(text);
                    }
                }
            },
        };
        using var source = new ActivitySource(TestSource);
        ActivitySource.AddActivityListener(listener);
        using var root = source.StartActivity("request", ActivityKind.Internal, parentContext: default).ShouldNotBeNull();
        trace = root.TraceId;

        // the in-process server starts its own trace unless the request carries one (W3C trace context): the request continues the test's
        client.DefaultRequestHeaders.Add("traceparent", root.Id);
        try
        {
            await request();
        }
        finally
        {
            client.DefaultRequestHeaders.Remove("traceparent");
        }

        statements.ShouldNotBeEmpty("the request's own statements were not seen: the trace did not reach the driver");
        return statements;
    }
}

using System.Data.Common;
using System.Net.Sockets;

namespace XDrishti.Application.Learning;

/// <summary>
/// Whether a failure is the kind that goes away on its own (a database or a network that went away, a timeout), read by walking the whole inner-exception chain: an EF retry-limit exception wraps the Npgsql exception that caused it, so the type of the outer exception alone says nothing. Used by the model hold-out look to answer a
/// failed pass after the claim as "interrupted, ask again" instead of an error of the server (CF-10e part 3, ADR 0285).
/// </summary>
internal static class TransientFailure
{
    /// <summary>True when the exception or anything it wraps is a database, I/O, socket or timeout failure, or an EF retry-limit exception.</summary>
    public static bool Is(Exception? failure)
    {
        for (var depth = 0; failure is not null && depth < 16; depth++, failure = failure.InnerException)
        {
            if (failure is DbException or IOException or SocketException or TimeoutException || failure.GetType().Name == "RetryLimitExceededException")
            {
                return true;
            }

            if (failure is AggregateException aggregate && aggregate.InnerExceptions.Any(Is))
            {
                return true;
            }
        }

        return false;
    }
}

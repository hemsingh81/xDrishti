using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using XDrishti.Api.Infrastructure;
using XDrishti.Application.Ai;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The server-sent events of a turn (spec P9-03 AC 3 and 11): the turn runs on its own task and writes its events to a channel; the response reads them and sends a <c>ping</c> when nothing came for the configured seconds. When the client goes away the turn is cancelled (the model connection
/// closes) and the response waits for the turn to store what it has, so that the single turn slot is released only after the audit rows are written. The event name is the type; the data is the JSON of the event.
/// </summary>
internal static class AssistantStream
{
    public static async IAsyncEnumerable<SseItem<string>> EventsAsync(ITurnSession session, TimeSpan ping, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TurnEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var run = Task.Run(async () =>
        {
            try
            {
                await session.RunAsync(e => channel.Writer.WriteAsync(e, CancellationToken.None), stop.Token);
            }
            finally
            {
                channel.Writer.TryComplete();
                await session.DisposeAsync();
            }
        }, CancellationToken.None);

        try
        {
            while (true)
            {
                var quiet = false;
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(ping);
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(wait.Token))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    quiet = true;
                }

                if (quiet)
                {
                    yield return Item(new TurnPing());
                    continue;
                }

                while (channel.Reader.TryRead(out var turnEvent))
                {
                    yield return Item(turnEvent);
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await run;
            }
            catch (Exception)
            {
                // The turn stored what it could; the response is over either way.
            }
        }
    }

    private static SseItem<string> Item(TurnEvent turnEvent) =>
        new(JsonSerializer.Serialize(turnEvent, turnEvent.GetType(), ApiJsonContext.Default), turnEvent.EventName());
}

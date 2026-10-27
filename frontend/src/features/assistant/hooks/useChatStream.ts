import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { api, type Problem } from '@/shared/api';
import { createSseParser } from '../lib/sseParser';
import { idleTurn, turnReducer, type TurnAction, type TurnState } from '../lib/turnReducer';
import { refusals } from '../lib/wording';

export interface ChatStream {
  turn: TurnState;
  /** The owner's question of the live turn (shown at once, before the stored conversation replaces it). */
  question: string | null;
  /** The sentence for a refusal that came before the stream (nothing was stored); the typed text is kept by the composer. */
  refusal: string | null;
  /** Resolves false when the endpoint refused before streaming (nothing was stored). */
  send: (message: string, conversationId: string | null) => Promise<boolean>;
  stop: () => void;
  clear: () => void;
}

const refusalText = (status: number, problem: Problem | undefined): string => {
  const code = problem?.code ?? '';
  if (status === 429) return refusals['auth.rate_limited'] ?? 'Too many requests.';
  return refusals[code] ?? problem?.title ?? 'The assistant could not take that question.';
};

/**
 * Runs one turn: `POST /api/assistant/chat` through the shared client (CSRF and the 401 hook apply), reads the body as a
 * stream, parses the frames and feeds the pure reducer. Frames are applied at most once per animation frame. A turn is never
 * retried; Stop aborts the request.
 */
export function useChatStream(onDone: (conversationId: string) => void): ChatStream {
  const [turn, dispatch] = useReducer(turnReducer, idleTurn);
  const [question, setQuestion] = useState<string | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  const queue = useRef<TurnAction[]>([]);
  const frame = useRef<number | null>(null);
  const doneRef = useRef(onDone);
  doneRef.current = onDone;

  const flush = useCallback(() => {
    if (frame.current !== null) cancelAnimationFrame(frame.current);
    frame.current = null;
    const actions = queue.current;
    queue.current = [];
    for (const action of actions) dispatch(action);
  }, []);

  const enqueue = useCallback(
    (action: TurnAction) => {
      queue.current.push(action);
      frame.current ??= requestAnimationFrame(flush);
    },
    [flush],
  );

  useEffect(
    () => () => {
      controller.current?.abort();
      if (frame.current !== null) cancelAnimationFrame(frame.current);
    },
    [],
  );

  const send = useCallback(
    async (message: string, conversationId: string | null) => {
      const abort = new AbortController();
      controller.current = abort;
      setRefusal(null);
      setQuestion(message);
      dispatch({ type: 'start' });
      const seen = { conversation: conversationId, finished: false };
      const parser = createSseParser((f) => {
        let data: unknown;
        try {
          data = JSON.parse(f.data);
        } catch {
          return; // a frame that is not JSON is ignored
        }
        if (f.event === 'turn' && typeof data === 'object' && data !== null && 'conversationId' in data) {
          const id = data.conversationId;
          if (typeof id === 'string') seen.conversation = id;
        }
        if (f.event === 'done') seen.finished = true;
        enqueue({ type: 'frame', event: f.event, data });
      });
      try {
        const result = await api.POST('/api/assistant/chat', {
          body: { conversationId, message },
          parseAs: 'stream',
          signal: abort.signal,
        });
        const body = result.data as ReadableStream<Uint8Array> | null | undefined;
        if (!result.response.ok || !body) {
          flush();
          dispatch({ type: 'reset' });
          setQuestion(null);
          setRefusal(refusalText(result.response.status, result.error));
          return false;
        }
        const reader = body.getReader();
        const decoder = new TextDecoder();
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          parser.push(decoder.decode(value, { stream: true }));
        }
        parser.push(decoder.decode());
        parser.end();
        enqueue({ type: 'closed' });
        flush();
      } catch {
        parser.end();
        enqueue({ type: abort.signal.aborted ? 'stopped' : 'closed' });
        flush();
      } finally {
        controller.current = null;
      }
      if (seen.finished && seen.conversation) doneRef.current(seen.conversation);
      return true;
    },
    [enqueue, flush],
  );

  const stop = useCallback(() => {
    controller.current?.abort();
  }, []);

  const clear = useCallback(() => {
    flush();
    dispatch({ type: 'reset' });
    setQuestion(null);
    setRefusal(null);
  }, [flush]);

  return { turn, question, refusal, send, stop, clear };
}

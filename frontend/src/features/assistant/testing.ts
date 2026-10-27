import { http, HttpResponse } from 'msw';
import { problemResponse, server } from '@/test/server';
import type { AssistantStatus, ConversationDetail, StoredToolCall } from './api/assistantApi';

/** Test builders for the assistant screen (tests only; not part of the feature's public API). */
export function statusBuilder(over: Partial<AssistantStatus> = {}): AssistantStatus {
  return {
    enabled: true,
    portfolioContext: false,
    maxToolCalls: 6,
    settingsId: 7,
    llm: {
      state: 'ready',
      model: 'xd-main',
      modelFile: null,
      modelSha256: null,
      build: null,
      checkedAt: '2026-10-27T09:00:00Z',
      detail: 'ready',
      startCommand: 'scripts/xd-ai.sh start',
    },
    tools: { state: 'ready', count: 4, schemaTokensEstimate: 900 },
    busy: false,
    nightlyRunning: false,
    evaluation: { state: 'not_yet_evaluated', at: null, detail: null },
    ...over,
  };
}

export function llm(state: string): AssistantStatus['llm'] {
  return { ...statusBuilder().llm, state };
}

export function mountStatus(status: AssistantStatus) {
  server.use(http.get('/api/assistant/status', () => HttpResponse.json(status)));
}

export function mountEmptyHistory() {
  server.use(http.get('/api/assistant/conversations', () => HttpResponse.json([])));
}

/** A streaming response made of `text`, cut into pieces of `size` bytes (so pieces can split a line or a UTF-8 character). */
export function sseResponse(text: string, { size = 37, hang = false }: { size?: number; hang?: boolean } = {}) {
  const bytes = new TextEncoder().encode(text);
  let at = 0;
  const body = new ReadableStream<Uint8Array>({
    pull(controller) {
      if (at >= bytes.length) {
        if (!hang) controller.close();
        return hang ? new Promise<void>(() => undefined) : undefined;
      }
      controller.enqueue(bytes.slice(at, at + size));
      at += size;
      return undefined;
    },
  });
  return new HttpResponse(body, { headers: { 'content-type': 'text/event-stream' } });
}

export function mountChat(response: () => Response) {
  server.use(http.post('/api/assistant/chat', response));
}

export function mountRefusal(status: number, code: string, title = 'Refused') {
  server.use(http.post('/api/assistant/chat', () => problemResponse({ title, code }, status)));
}

export const conversationId = '0198a2c4-7b1e-7c3a-9a41-5f0c6d2e8b10';

export function conversationBuilder(over: Partial<ConversationDetail> = {}): ConversationDetail {
  return {
    id: conversationId,
    title: 'Expectancy question',
    origin: 'chat',
    createdAt: '2026-10-27T08:00:00Z',
    modelId: 'xd-main',
    settingsId: 7,
    messages: [
      {
        id: 101,
        turnId: 't1',
        seq: 1,
        role: 'user',
        content: 'What is the expectancy?',
        createdAt: '2026-10-27T08:00:00Z',
        finishReason: null,
        groundingStatus: null,
        ungrounded: [],
        promptTokens: null,
        completionTokens: null,
        firstTokenMs: null,
        totalMs: null,
        promptHash: null,
        noAnswerStored: false,
        toolCalls: [],
      },
      {
        id: 502,
        turnId: 't1',
        seq: 2,
        role: 'assistant',
        content: 'Expectancy is 0.12R over 143 trades [1], and about 46 of them won.',
        createdAt: '2026-10-27T08:00:04Z',
        finishReason: 'stop',
        groundingStatus: 'ungrounded',
        ungrounded: [{ start: 51, end: 53, text: '46' }],
        promptTokens: 10,
        completionTokens: 20,
        firstTokenMs: 100,
        totalMs: 400,
        promptHash: 'h',
        noAnswerStored: false,
        toolCalls: [
          {
            id: 501,
            index: 1,
            tool: 'get_report',
            arguments: { report: 'summary' },
            status: 'ok',
            errorCode: null,
            rows: 2,
            truncated: false,
            durationMs: 42,
            evidence: null,
          },
        ],
      },
    ],
    ...over,
  };
}

export function toolCallBuilder(over: Partial<StoredToolCall> = {}): StoredToolCall {
  return {
    id: 501,
    conversationId,
    turnId: 't1',
    callIndex: 1,
    tool: 'get_report',
    arguments: { report: 'summary' },
    status: 'ok',
    errorCode: null,
    result: {
      ok: true,
      asOf: '2026-10-27T03:00:00Z',
      rows: [
        { basket: 'Nifty 50', expectancy: { state: 'value', number: 0.12, reason: null }, n: 143 },
        { basket: 'Bank', expectancy: { state: 'insufficient', number: null, reason: 'Only 4 trades.' }, n: 4 },
      ],
      totalRows: 2,
    },
    resultBytes: 100,
    rows: 2,
    truncated: false,
    evidence: { basis: 'gross', n: 143, dateRange: '2025-01-01..2025-12-31' },
    redactions: 0,
    injectionFlags: 0,
    startedAt: '2026-10-27T08:00:01Z',
    durationMs: 42,
    ...over,
  };
}

export function mountConversation(detail: ConversationDetail = conversationBuilder()) {
  server.use(
    http.get('/api/assistant/conversations', () =>
      HttpResponse.json([
        {
          id: detail.id,
          title: detail.title,
          origin: 'chat',
          createdAt: detail.createdAt,
          messageCount: detail.messages.length,
          lastActivityAt: detail.createdAt,
        },
      ]),
    ),
    http.get('/api/assistant/conversations/:id', () => HttpResponse.json(detail)),
    http.get('/api/assistant/tool-calls/:id', () => HttpResponse.json(toolCallBuilder())),
  );
}

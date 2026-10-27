import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type AssistantStatus = Schemas['AssistantStatusView'];
export type SettingsRequest = Schemas['AssistantSettingsRequest'];
export type ConversationSummary = Schemas['ConversationSummary'];
export type ConversationDetail = Schemas['ConversationDetail'];
export type StoredToolCall = Schemas['StoredToolCall'];
export type StoredMessage = Schemas['MessageView'];

/** True when a question can be asked now: every part is up and no other turn runs. */
export const isReady = (s: AssistantStatus) =>
  s.enabled && s.llm.state === 'ready' && s.tools.state === 'ready' && !s.busy && !s.nightlyRunning;

/** Polled every 30 s while ready, every 3 s while the model loads, every 5 s otherwise (the screen switches by itself). */
export const statusQuery = queryOptions({
  queryKey: ['assistant', 'status'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/assistant/status', { signal })),
  staleTime: 5_000,
  refetchInterval: (query) => {
    const s = query.state.data;
    if (!s) return 5_000;
    if (isReady(s)) return 30_000;
    return s.enabled && s.llm.state === 'loading' ? 3_000 : 5_000;
  },
});

export const conversationsQuery = queryOptions({
  queryKey: ['assistant', 'conversations'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/assistant/conversations', { signal })),
  staleTime: 30_000,
});

export const conversationQuery = (id: string) =>
  queryOptions({
    queryKey: ['assistant', 'conversation', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/assistant/conversations/{id}', { params: { path: { id } }, signal })),
  });

/** A stored tool call never changes (the audit is insert-only): cached for good. */
export const toolCallQuery = (id: number) =>
  queryOptions({
    queryKey: ['assistant', 'tool-call', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/assistant/tool-calls/{id}', { params: { path: { id } }, signal })),
    staleTime: Infinity,
  });

/** The owner act: the password (when the change raises exposure) travels in this body only and is not kept. */
export function useChangeSettings() {
  const client = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (body: SettingsRequest) => unwrap(await api.PUT('/api/assistant/settings', { body })),
    onSuccess: (status) => {
      client.setQueryData(statusQuery.queryKey, status);
    },
    onSettled: () => client.invalidateQueries({ queryKey: statusQuery.queryKey }),
  });
}

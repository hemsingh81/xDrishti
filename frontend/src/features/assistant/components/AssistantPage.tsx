import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import SettingsIcon from '@mui/icons-material/SettingsOutlined';
import { Box, Button, Drawer, Stack, useMediaQuery, useTheme } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { conversationQuery, conversationsQuery } from '../api/assistantApi';
import { useAssistantStatus } from '../hooks/useAssistantStatus';
import { useChatStream } from '../hooks/useChatStream';
import { gateOf } from '../lib/gate';
import { words } from '../lib/wording';
import { Composer } from './Composer';
import { DraftPanelLazy } from './DraftPanelLazy';
import { ConversationList } from './ConversationList';
import { EvaluationLine } from './EvaluationLine';
import { MessageList } from './MessageList';
import { SettingsDialog } from './SettingsDialog';
import { StatusBanner } from './StatusBanner';

/** The Assistant screen: history on the left, the thread and the question box on the right. Read-only apart from asking. */
export function AssistantPage() {
  const status = useAssistantStatus();
  const client = useQueryClient();
  const [params, setParams] = useSearchParams();
  const selected = params.get('c');
  const stored = useQuery({ ...conversationQuery(selected ?? ''), enabled: selected !== null });
  const [draft, setDraft] = useState('');
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [draftsOpen, setDraftsOpen] = useState(false);
  const input = useRef<HTMLTextAreaElement>(null);
  const theme = useTheme();
  const wide = useMediaQuery(theme.breakpoints.up('md'));

  const stream = useChatStream(
    useCallback(
      (id: string) => {
        void (async () => {
          try {
            await client.query({ ...conversationQuery(id), staleTime: 0 });
            await client.invalidateQueries({ queryKey: conversationsQuery.queryKey });
            setParams({ c: id }, { replace: true });
          } catch {
            // The stored copy could not be read: the live answer stays on screen.
          }
        })();
      },
      [client, setParams],
    ),
  );

  const running = stream.turn.running;
  // The stored conversation replaces the live copy once it holds the answer (the list hides the live copy meanwhile).
  const doneId = stream.turn.done?.messageId ?? null;
  const storedHasAnswer = doneId !== null && (stored.data?.messages.some((m) => m.id === doneId) ?? false);
  const { clear } = stream;
  useEffect(() => {
    if (storedHasAnswer) clear();
  }, [storedHasAnswer, clear]);
  const wasRunning = useRef(false);
  useEffect(() => {
    if (wasRunning.current && !running) input.current?.focus();
    wasRunning.current = running;
  }, [running]);

  const gate = status.data ? gateOf(status.data) : null;

  const send = () => {
    const text = draft;
    setDraft('');
    // A refusal keeps the typed text.
    void stream.send(text, selected).then((accepted) => {
      if (!accepted) setDraft(text);
    });
  };

  const select = (id: string | null) => {
    stream.clear();
    setParams(id ? { c: id } : {});
    setDrawerOpen(false);
  };

  const announcement = running ? words.working : stream.turn.done ? words.ready : (stream.refusal ?? '');
  const list = (
    <ConversationList
      selected={selected}
      onSelect={select}
      onNew={() => {
        select(null);
      }}
    />
  );
  const live = stream.question === null ? null : { turn: stream.turn, question: stream.question };

  return (
    <Box>
      <PageHeader
        title={words.title}
        description="Ask questions about your data. Answers come from tool calls you can open; figures not found in them are marked."
      />
      <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
        {!wide && (
          <Button
            startIcon={<HistoryIcon />}
            onClick={() => {
              setDrawerOpen(true);
            }}
          >
            History
          </Button>
        )}
        <Button
          startIcon={<SettingsIcon />}
          disabled={!status.data}
          onClick={() => {
            setSettingsOpen(true);
          }}
        >
          Settings
        </Button>
        <Button
          aria-expanded={draftsOpen}
          onClick={() => {
            setDraftsOpen((open) => !open);
          }}
        >
          Draft a strategy
        </Button>
      </Stack>
      {status.isPending && <LoadingState label="Loading assistant status…" />}
      {status.isError && <ErrorState error={status.error} />}
      {status.data && gate && (
        <>
          <StatusBanner
            gate={gate}
            onTurnOn={() => {
              setSettingsOpen(true);
            }}
          />
          <EvaluationLine evaluation={status.data.evaluation} />
        </>
      )}
      <Box sx={{ display: 'grid', gridTemplateColumns: wide ? '260px minmax(0, 1fr)' : 'minmax(0, 1fr)', gap: 2 }}>
        {wide && list}
        <Box sx={{ display: 'flex', flexDirection: 'column', minWidth: 0, minHeight: '50vh' }}>
          {selected && stored.isPending && <LoadingState label="Loading conversation…" />}
          {selected && stored.isError && <ErrorState error={stored.error} />}
          <MessageList
            messages={selected ? (stored.data?.messages ?? []) : []}
            live={live}
            refusal={stream.refusal}
            onExample={(text) => {
              setDraft(text);
              input.current?.focus();
            }}
          />
          <Composer
            value={draft}
            onChange={setDraft}
            onSend={send}
            onStop={stream.stop}
            running={running}
            disabledReason={gate ? (gate.canAsk ? null : gate.title) : 'Checking the assistant…'}
            inputRef={input}
          />
        </Box>
      </Box>
      {draftsOpen && <DraftPanelLazy />}
      <Box role="status" aria-live="polite" sx={visuallyHidden}>
        {announcement}
      </Box>
      {!wide && (
        <Drawer
          open={drawerOpen}
          onClose={() => {
            setDrawerOpen(false);
          }}
          slotProps={{ paper: { sx: { width: 280, p: 2 } } }}
        >
          {list}
        </Drawer>
      )}
      {status.data && (
        <SettingsDialog
          open={settingsOpen}
          status={status.data}
          onClose={() => {
            setSettingsOpen(false);
          }}
        />
      )}
    </Box>
  );
}

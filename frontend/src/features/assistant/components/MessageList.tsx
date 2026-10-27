import { Box, Button, Stack, Typography } from '@mui/material';
import type { StoredMessage } from '../api/assistantApi';
import { answerOf, type TurnState } from '../lib/turnReducer';
import { words } from '../lib/wording';
import { AssistantMessage } from './AssistantMessage';
import type { ChipCall } from './ToolCallChips';
import { UserMessage } from './UserMessage';

export interface LiveTurn {
  turn: TurnState;
  question: string;
}

const workingText = (turn: TurnState): string | null => {
  if (!turn.running) return null;
  if (turn.phase === 'tool') return `Working: calling ${turn.tool ?? 'a tool'}`;
  return turn.phase === 'answering' ? 'Working: writing the answer' : 'Working: reading the question';
};

function storedNotes(m: StoredMessage): string[] {
  if (m.finishReason === 'cancelled') return [words.stopped];
  if (m.finishReason === 'error') return ['The turn failed'];
  return [];
}

function Stored({ m }: { m: StoredMessage }) {
  if (m.role === 'user') {
    return (
      <>
        <UserMessage text={m.content} />
        {m.noAnswerStored && (
          <Typography variant="body2" color="text.secondary">
            No answer was stored for this question.
          </Typography>
        )}
      </>
    );
  }
  const calls: ChipCall[] = m.toolCalls.map((c) => ({
    id: c.id,
    index: c.index,
    tool: c.tool,
    rows: c.rows,
    status: c.status,
  }));
  return (
    <AssistantMessage
      text={m.content}
      calls={calls}
      grounding={{ status: m.groundingStatus ?? 'not_checked', spans: m.ungrounded }}
      notes={storedNotes(m)}
    />
  );
}

function Live({ live }: { live: LiveTurn }) {
  const { turn } = live;
  const calls: ChipCall[] = turn.calls.map((c) => ({
    id: c.toolCallId,
    index: c.index,
    tool: c.tool,
    rows: c.rows,
    status: c.complete ? (c.status ?? 'error') : null,
  }));
  const notes = [
    ...(turn.stopped ? [words.stopped] : []),
    ...(turn.interrupted ? [words.closedEarly, words.incomplete] : []),
    ...(turn.error ? [turn.error.message] : []),
  ];
  const grounding = turn.done
    ? { status: turn.done.grounding.status, spans: turn.done.grounding.ungrounded }
    : turn.running
      ? null
      : { status: 'not_checked', spans: [] };
  return (
    <>
      <UserMessage text={live.question} />
      <AssistantMessage
        text={answerOf(turn)}
        calls={calls}
        grounding={grounding}
        notes={notes}
        streaming={turn.running}
        working={workingText(turn)}
      />
    </>
  );
}

interface Props {
  messages: readonly StoredMessage[];
  live: LiveTurn | null;
  refusal: string | null;
  onExample: (text: string) => void;
}

/** The thread: a log that does not announce tokens (a separate status region says "working" and "ready"). */
export function MessageList({ messages, live, refusal, onExample }: Props) {
  // After `done` the stored conversation arrives; if it already holds the answer, the live copy is dropped.
  const doneId = live?.turn.done?.messageId ?? null;
  const stored = doneId !== null && messages.some((m) => m.id === doneId);
  const empty = messages.length === 0 && !live;
  return (
    <Stack role="log" aria-live="off" aria-label="Conversation" spacing={1.5} sx={{ flex: 1, minWidth: 0, py: 1 }}>
      {empty && (
        <Box>
          <Typography color="text.secondary" sx={{ mb: 1 }}>
            Ask about your data. Examples (they fill the box and do not send):
          </Typography>
          <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
            {words.examples.map((e) => (
              <Button
                key={e}
                size="small"
                variant="outlined"
                onClick={() => {
                  onExample(e);
                }}
              >
                {e}
              </Button>
            ))}
          </Stack>
        </Box>
      )}
      {messages.map((m) => (
        <Stored key={m.id} m={m} />
      ))}
      {live && !stored && <Live live={live} />}
      {refusal && (
        <Typography variant="body2" color="warning.main">
          {refusal}
        </Typography>
      )}
    </Stack>
  );
}

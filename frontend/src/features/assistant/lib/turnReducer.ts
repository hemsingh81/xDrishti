export interface Grounding {
  status: string;
  checked: number;
  ungrounded: { start: number; end: number; text: string }[];
}

export interface LiveCall {
  index: number;
  tool: string;
  arguments: unknown;
  complete: boolean;
  toolCallId: number | null;
  status: string | null;
  rows: number | null;
  truncated: boolean;
  evidence: unknown;
  error: { code: string; message: string } | null;
}

export interface TurnDone {
  messageId: number | null;
  finishReason: string;
  grounding: Grounding;
}

export interface TurnState {
  /** True from the question until `done`, `error`, a close or a stop. */
  running: boolean;
  phase: 'reading' | 'tool' | 'answering';
  tool: string | null;
  conversationId: string | null;
  calls: LiveCall[];
  /** The text per model iteration; a discarded iteration is removed. */
  iterations: Record<number, string>;
  done: TurnDone | null;
  error: { code: string; message: string } | null;
  /** The stream closed before `done` (and was not stopped). */
  interrupted: boolean;
  stopped: boolean;
  /** The answer reached the length cap and the rest was dropped. */
  truncated: boolean;
  /** Characters kept in `iterations` (for the cap). */
  length: number;
}

export type TurnAction =
  | { type: 'start' }
  | { type: 'frame'; event: string; data: unknown }
  | { type: 'closed' }
  | { type: 'stopped' }
  | { type: 'reset' };

export const idleTurn: TurnState = {
  running: false,
  phase: 'reading',
  tool: null,
  conversationId: null,
  calls: [],
  iterations: {},
  done: null,
  error: null,
  interrupted: false,
  stopped: false,
  truncated: false,
  length: 0,
};

/** The most answer text kept; the rest is dropped and the answer says so. */
export const maxAnswer = 200_000;

type Rec = Record<string, unknown>;
const rec = (v: unknown): Rec => (typeof v === 'object' && v !== null && !Array.isArray(v) ? (v as Rec) : {});
const str = (v: unknown): string | null => (typeof v === 'string' ? v : null);
const int = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

/** The answer text so far: the iterations that were not discarded, in order. */
export function answerOf(state: Pick<TurnState, 'iterations'>): string {
  return Object.keys(state.iterations)
    .map(Number)
    .sort((a, b) => a - b)
    .map((k) => state.iterations[k] ?? '')
    .join('');
}

function groundingOf(value: unknown): Grounding {
  const g = rec(value);
  const spans = Array.isArray(g.ungrounded) ? g.ungrounded : [];
  return {
    status: str(g.status) ?? 'not_checked',
    checked: int(g.checked) ?? 0,
    ungrounded: spans.flatMap((s) => {
      const r = rec(s);
      const start = int(r.start);
      const end = int(r.end);
      return start === null || end === null ? [] : [{ start, end, text: str(r.text) ?? '' }];
    }),
  };
}

function onFrame(state: TurnState, event: string, raw: unknown): TurnState {
  const d = rec(raw);
  switch (event) {
    case 'turn':
      return { ...state, conversationId: str(d.conversationId) ?? state.conversationId };
    case 'status': {
      const phase = str(d.phase);
      if (phase !== 'reading' && phase !== 'tool' && phase !== 'answering') return state;
      return { ...state, phase, tool: str(d.tool) };
    }
    case 'tool_call': {
      const index = int(d.callIndex);
      const tool = str(d.tool);
      if (index === null || tool === null) return state;
      const call: LiveCall = {
        index,
        tool,
        arguments: d.arguments,
        complete: false,
        toolCallId: null,
        status: null,
        rows: null,
        truncated: false,
        evidence: null,
        error: null,
      };
      return { ...state, phase: 'tool', tool, calls: [...state.calls.filter((c) => c.index !== index), call] };
    }
    case 'tool_result': {
      const index = int(d.callIndex);
      if (index === null) return state;
      const err = d.error === null || d.error === undefined ? null : rec(d.error);
      return {
        ...state,
        calls: state.calls.map((c) =>
          c.index === index
            ? {
                ...c,
                complete: true,
                toolCallId: int(d.toolCallId),
                status: str(d.status) ?? 'error',
                rows: int(d.rowCount),
                truncated: d.truncated === true,
                evidence: d.evidence ?? null,
                error: err ? { code: str(err.code) ?? 'error', message: str(err.message) ?? '' } : null,
              }
            : c,
        ),
      };
    }
    case 'token': {
      const iteration = int(d.iteration);
      const text = str(d.text);
      if (iteration === null || text === null) return state;
      if (state.length + text.length > maxAnswer) return { ...state, truncated: true };
      return {
        ...state,
        length: state.length + text.length,
        iterations: { ...state.iterations, [iteration]: (state.iterations[iteration] ?? '') + text },
      };
    }
    case 'iteration_discard': {
      const iteration = int(d.iteration);
      if (iteration === null) return state;
      const rest = Object.fromEntries(Object.entries(state.iterations).filter(([k]) => Number(k) !== iteration));
      return { ...state, iterations: rest, length: state.length - (state.iterations[iteration]?.length ?? 0) };
    }
    case 'done':
      return {
        ...state,
        running: false,
        done: {
          messageId: int(d.messageId),
          finishReason: str(d.finishReason) ?? 'stop',
          grounding: groundingOf(d.grounding),
        },
      };
    case 'error':
      return {
        ...state,
        running: false,
        error: { code: str(d.code) ?? 'error', message: str(d.message) ?? 'The turn failed.' },
      };
    default:
      return state; // ping and anything unknown
  }
}

export function turnReducer(state: TurnState, action: TurnAction): TurnState {
  switch (action.type) {
    case 'start':
      return { ...idleTurn, running: true };
    case 'frame':
      return state.running ? onFrame(state, action.event, action.data) : state;
    case 'closed':
      return state.running ? { ...state, running: false, interrupted: true } : state;
    case 'reset':
      return idleTurn;
    case 'stopped':
      return state.running ? { ...state, running: false, stopped: true } : state;
  }
}

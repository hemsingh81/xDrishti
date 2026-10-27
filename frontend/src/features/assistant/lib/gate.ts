import type { AssistantStatus } from '../api/assistantApi';
import { words } from './wording';

export type GateKind = 'off' | 'down' | 'loading' | 'misconfigured' | 'tools_down' | 'busy' | 'nightly' | 'ready';

export interface Gate {
  kind: GateKind;
  title: string;
  help?: string;
  /** A command to show in a copyable code block. */
  command?: string;
  /** The composer is disabled with `reason` as visible text. */
  canAsk: boolean;
}

/** What the screen says about the status: one gate per state, first match wins. Never a fake answer, never an endless spinner. */
export function gateOf(s: AssistantStatus): Gate {
  if (!s.enabled) return { kind: 'off', title: words.off, help: words.offHelp, canAsk: false };
  switch (s.llm.state) {
    case 'down':
      return {
        kind: 'down',
        title: words.down,
        help: words.downHelp,
        command: s.llm.startCommand || words.startCommand,
        canAsk: false,
      };
    case 'loading':
      return { kind: 'loading', title: words.loading, canAsk: false };
    case 'misconfigured':
      return { kind: 'misconfigured', title: words.misconfigured, help: words.misconfiguredHelp, canAsk: false };
    default:
      break;
  }
  if (s.tools.state !== 'ready') {
    return {
      kind: 'tools_down',
      title: words.toolsDown,
      help: words.toolsDownHelp,
      command: words.startCommand,
      canAsk: false,
    };
  }
  if (s.nightlyRunning) return { kind: 'nightly', title: words.nightly, canAsk: false };
  if (s.busy) return { kind: 'busy', title: words.busy, canAsk: false };
  return { kind: 'ready', title: '', canAsk: true };
}

import {
  llm,
  mountConversation,
  mountEmptyHistory,
  mountStatus,
  statusBuilder,
} from '../src/features/assistant/testing';
import type { Screen } from './matrix';

/** P9-04: the Assistant screen, answered by the MSW handlers of the feature's test helpers. */
export const assistantScreen: Screen = {
  id: 'P9-04-assistant',
  route: '/assistant',
  mounts: {
    full: () => {
      mountStatus(statusBuilder());
      mountConversation();
    },
    empty: () => {
      mountStatus(statusBuilder());
      mountEmptyHistory();
    },
    unavailable: () => {
      mountStatus(statusBuilder({ llm: llm('down') }));
      mountEmptyHistory();
    },
  },
};

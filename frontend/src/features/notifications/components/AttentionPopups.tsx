import { useAttentionPopups } from '../hooks/useAttentionPopups';
import type { ReadStorage } from '../lib/readState';

export interface AttentionPopupsProps {
  /** Where read state and "already popped up" are kept; defaults to localStorage. Tests pass a fake, `null` turns it off. */
  storage?: ReadStorage | null;
}

/** Renders nothing: mounts the attention pop-ups once, in the app shell (see `useAttentionPopups`). */
export function AttentionPopups({ storage }: AttentionPopupsProps) {
  useAttentionPopups(storage);
  return null;
}

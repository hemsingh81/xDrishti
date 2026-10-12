import type { ReactNode } from 'react';
import { useAccountsNeedingAction } from '@/features/accounts';
import type { NavBadgeId } from '../navigation';

/** A count bubble for a navigation item, with the words for assistive technology and the tooltip. */
export interface NavBadge {
  count: number;
  /** Colour of the bubble; the count and the accessible name carry the meaning too. */
  severity: 'error' | 'warning';
  /** Appended to the item's name for assistive technology: "Accounts, 1 needs reconnecting". */
  label: string;
  /** Hover / focus text: one line per thing that needs doing. */
  lines: string[];
}

interface SourceProps {
  children: (badge: NavBadge | null) => ReactNode;
}

const verb = (count: number) => (count === 1 ? 'needs' : 'need');
const advice = {
  expired: 'expired, open Accounts to reconnect',
  not_connected: 'not connected, open Accounts to connect',
};

/** Accounts whose broker connection expired (red) or is not connected (amber). Nothing while loading, on error or when all are connected. */
function AccountsAttention({ children }: SourceProps) {
  const { count, expired, items } = useAccountsNeedingAction();
  if (count === 0) {
    return children(null);
  }
  return children({
    count,
    severity: expired ? 'error' : 'warning',
    label: `${String(count)} ${verb(count)} reconnecting`,
    lines: items.map((i) => `${i.name} — ${advice[i.status]}`),
  });
}

const sources: Record<NavBadgeId, (props: SourceProps) => ReactNode> = { 'accounts-attention': AccountsAttention };

/** Resolves a navigation item's badge id to live data and hands it to `children` (render-prop, so hooks stay per item). */
export function NavBadgeSource({ id, children }: SourceProps & { id: NavBadgeId }) {
  const Source = sources[id];
  return <Source>{children}</Source>;
}

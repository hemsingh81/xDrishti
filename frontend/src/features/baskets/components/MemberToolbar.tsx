import { Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { BasketMember } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useMemberPanel } from '../hooks/memberPanelContext';
import { subject } from '../lib/messages';
import { TransferMenu } from './TransferMenu';

interface MemberToolbarProps {
  members: readonly BasketMember[];
  selectedIds: ReadonlySet<string>;
  onClear: () => void;
}

type Verb = 'copy' | 'move';

/**
 * A slim row above the grid, there only while something is selected: how many, Clear selection, and the actions over
 * them: Copy to…, Move to… (menus of the other baskets) and Remove.
 */
export function MemberToolbar({ members, selectedIds, onClear }: MemberToolbarProps) {
  const { basket, others, busy, transfer, remove } = useMemberPanel();
  const [menu, setMenu] = useState<{ anchor: HTMLElement; mode: Verb } | null>(null);
  const picked = members.filter((m) => selectedIds.has(m.instrument.id));
  const ids = picked.map((m) => m.instrument.id);
  const count = ids.length;
  const label = subject(count, picked[0]?.instrument.symbol ?? '');
  // A portfolio basket's rows can only be copied out: no Move to…, no Remove.
  const readOnly = basket.system;
  const guard = (action: () => void) => () => {
    if (!busy) action();
  };
  const open = (mode: Verb) => (event: { currentTarget: HTMLElement }) => {
    if (!busy) setMenu({ anchor: event.currentTarget, mode });
  };

  if (count === 0) return null;

  return (
    <Stack
      direction="row"
      sx={{
        flexWrap: 'wrap',
        gap: 1.5,
        alignItems: 'center',
        justifyContent: 'space-between',
        px: 2,
        py: 0.75,
        borderBottom: 1,
        borderColor: 'divider',
      }}
    >
      <Stack direction="row" sx={{ columnGap: 1, alignItems: 'center' }}>
        <Typography role="status" sx={{ fontWeight: 600 }}>
          {`${formatNumber(count)} selected`}
        </Typography>
        <Button size="small" onClick={onClear}>
          Clear selection
        </Button>
      </Stack>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        {(readOnly ? (['copy'] as const) : (['copy', 'move'] as const)).map((mode) => (
          <Button
            key={mode}
            size="small"
            variant="outlined"
            aria-haspopup="menu"
            disabled={others.length === 0}
            aria-disabled={busy || undefined}
            onClick={open(mode)}
          >
            {mode === 'copy' ? 'Copy to…' : 'Move to…'}
          </Button>
        ))}
        {!readOnly && (
          <Button
            size="small"
            variant="outlined"
            aria-disabled={busy || undefined}
            onClick={guard(() => {
              void remove(ids, label).then((done) => {
                if (done) onClear();
              });
            })}
          >
            {`Remove (${formatNumber(count)})`}
          </Button>
        )}
      </Stack>
      <TransferMenu
        anchor={menu?.anchor ?? null}
        modes={menu ? [menu.mode] : []}
        baskets={others}
        label={
          menu?.mode === 'move'
            ? `Move to another basket from ${basket.name}`
            : `Copy to another basket from ${basket.name}`
        }
        onPick={(toId, mode) => {
          setMenu(null);
          void transfer(ids, toId, mode, label).then((done) => {
            if (done) onClear();
          });
        }}
        onClose={() => {
          setMenu(null);
        }}
      />
    </Stack>
  );
}

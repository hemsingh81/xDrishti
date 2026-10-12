import CloseIcon from '@mui/icons-material/Close';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMoveOutlined';
import { IconButton, Stack } from '@mui/material';
import { useState } from 'react';
import type { BasketMember } from '@/shared/api';
import { useMemberPanel } from '../hooks/memberPanelContext';
import { TransferMenu } from './TransferMenu';

const bothModes = ['copy', 'move'] as const;
const copyOnly = ['copy'] as const;

/**
 * The buttons at the end of a member row: "Copy or move …" (a menu of the other baskets) and "Remove … from …". A
 * portfolio basket's rows only have "Copy …": they are the account's holdings and cannot be moved or removed.
 */
export function MemberActions({ member }: { member: BasketMember }) {
  const { basket, others, busy, transfer, remove } = useMemberPanel();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const { id, symbol } = member.instrument;
  const readOnly = basket.system;
  const title = readOnly ? `Copy ${symbol}` : `Copy or move ${symbol}`;
  return (
    <Stack direction="row" sx={{ justifyContent: 'center' }}>
      <IconButton
        size="small"
        aria-label={title}
        aria-haspopup="menu"
        aria-disabled={busy || undefined}
        disabled={others.length === 0}
        onClick={(event) => {
          if (!busy) setAnchor(event.currentTarget);
        }}
      >
        <DriveFileMoveIcon fontSize="small" />
      </IconButton>
      {!readOnly && (
        <IconButton
          size="small"
          aria-label={`Remove ${symbol} from ${basket.name}`}
          aria-disabled={busy || undefined}
          onClick={() => {
            if (!busy) void remove([id], symbol);
          }}
        >
          <CloseIcon fontSize="small" />
        </IconButton>
      )}
      <TransferMenu
        anchor={anchor}
        modes={readOnly ? copyOnly : bothModes}
        baskets={others}
        label={title}
        onPick={(toId, mode) => {
          setAnchor(null);
          void transfer([id], toId, mode, symbol);
        }}
        onClose={() => {
          setAnchor(null);
        }}
      />
    </Stack>
  );
}

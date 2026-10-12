import CloseIcon from '@mui/icons-material/Close';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMoveOutlined';
import { IconButton, Stack } from '@mui/material';
import { useState } from 'react';
import type { BasketMember } from '@/shared/api';
import { useMemberPanel } from '../hooks/memberPanelContext';
import { TransferMenu } from './TransferMenu';

const modes = ['copy', 'move'] as const;

/** The buttons at the end of a member row: "Copy or move …" (a menu of the other baskets) and "Remove … from …". */
export function MemberActions({ member }: { member: BasketMember }) {
  const { basket, others, busy, transfer, remove } = useMemberPanel();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const { id, symbol } = member.instrument;
  return (
    <Stack direction="row" sx={{ justifyContent: 'center' }}>
      <IconButton
        size="small"
        aria-label={`Copy or move ${symbol}`}
        aria-haspopup="menu"
        aria-disabled={busy || undefined}
        disabled={others.length === 0}
        onClick={(event) => {
          if (!busy) setAnchor(event.currentTarget);
        }}
      >
        <DriveFileMoveIcon fontSize="small" />
      </IconButton>
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
      <TransferMenu
        anchor={anchor}
        modes={modes}
        baskets={others}
        label={`Copy or move ${symbol}`}
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

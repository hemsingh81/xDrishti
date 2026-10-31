import { Alert, Button, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { Schemas } from '@/shared/api';
import { formatDuration, formatIstDateTime } from '@/shared/lib/format';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { problemText } from '../api/confirmApi';
import { useConfirmPromotion, useDemote, useRequestPromotion } from '../api/writesApi';
import { rungBelow } from '../lib/rungs';
import { modeWord } from '../lib/stateWords';
import { PasswordReasonDialog } from './PasswordReasonDialog';

type Account = Schemas['ExecutionModeAccountView'];

function useClock(ms: number): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => {
      setNow(Date.now());
    }, ms);
    return () => {
      clearInterval(id);
    };
  }, [ms]);
  return now;
}

/** Request, confirm (after the server's cooling-off) and move down. Moving up always asks the password. */
export function PromotionControls({ account }: { account: Account }) {
  const request = useRequestPromotion();
  const confirm = useConfirmPromotion();
  const demote = useDemote();
  const now = useClock(1000);
  const [dialog, setDialog] = useState<'request' | 'confirm' | 'demote' | null>(null);
  const next = account.next;
  const open = account.request;
  const below = rungBelow(account.effectiveMode);
  const eligibleAt = open ? Date.parse(open.eligibleFrom) : 0;
  const expiresAt = open ? Date.parse(open.expiresAt) : 0;
  const waitSeconds = Math.max(0, Math.ceil((eligibleAt - now) / 1000));
  const canConfirm = open !== null && now >= eligibleAt && now < expiresAt;
  const close = () => {
    setDialog(null);
  };

  return (
    <Stack sx={{ gap: 1 }}>
      <Typography variant="body2">
        Moving up needs your password twice, at least two sessions apart; moving down is instant.
      </Typography>
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button
          variant="outlined"
          disabled={next?.verdict !== 'eligible' || open !== null}
          onClick={() => {
            setDialog('request');
          }}
        >
          Request promotion
        </Button>
        {open && (
          <Button
            variant="contained"
            disabled={!canConfirm}
            onClick={() => {
              setDialog('confirm');
            }}
          >
            Confirm promotion
          </Button>
        )}
        {below && (
          <Button
            color="warning"
            variant="outlined"
            onClick={() => {
              setDialog('demote');
            }}
          >
            Move down one step
          </Button>
        )}
      </Stack>
      {open && (
        <Typography variant="body2" role="timer" aria-live="off">
          {now < eligibleAt
            ? `Cooling-off: confirming is possible in ${formatDuration(waitSeconds)} (from ${formatIstDateTime(open.eligibleFrom)} IST).`
            : now < expiresAt
              ? `Confirming is possible until ${formatIstDateTime(open.expiresAt)} IST.`
              : 'The request has expired; ask again.'}
        </Typography>
      )}
      {demote.isError && <Alert severity="error">{problemText(demote.error)}</Alert>}
      {next && (
        <PasswordReasonDialog
          open={dialog === 'request'}
          title={`Request promotion to ${modeWord(next.to)}`}
          confirmLabel="Request promotion"
          pending={request.isPending}
          error={request.error}
          onClose={close}
          onSubmit={(v) => {
            request.mutate({ accountId: account.accountId, body: { toMode: next.to, ...v } }, { onSuccess: close });
          }}
        />
      )}
      {open && (
        <PasswordReasonDialog
          open={dialog === 'confirm'}
          title={`Confirm promotion to ${modeWord(open.toMode)}`}
          confirmLabel="Confirm promotion"
          pending={confirm.isPending}
          error={confirm.error}
          onClose={close}
          onSubmit={(v) => {
            confirm.mutate(
              { accountId: account.accountId, id: open.id, body: { toMode: open.toMode, ...v } },
              { onSuccess: close },
            );
          }}
        />
      )}
      {below && (
        <ConfirmDialog
          open={dialog === 'demote'}
          title={`Move down to ${modeWord(below)}?`}
          confirmLabel={`Move down to ${modeWord(below)}`}
          busy={demote.isPending}
          onCancel={close}
          onConfirm={() => {
            demote.mutate(
              { accountId: account.accountId, body: { toMode: below, reason: 'Moved down by the owner' } },
              { onSuccess: close },
            );
          }}
        >
          {account.name} goes from {modeWord(account.effectiveMode)} to {modeWord(below)} now. No password is needed.
        </ConfirmDialog>
      )}
    </Stack>
  );
}

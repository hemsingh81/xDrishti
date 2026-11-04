import LockOpenIcon from '@mui/icons-material/LockOpenOutlined';
import { Alert, Box, Button, Chip, Typography } from '@mui/material';
import { useState } from 'react';
import { formatDate } from '@/shared/lib/format';
import type { ReportHoldOut } from '../api/confidenceQueries';
import { HoldOutJobStatus } from './HoldOutJobStatus';
import { HoldOutNumbers } from './HoldOutNumbers';
import { UnlockDialog } from './UnlockDialog';

const stateWord: Record<string, string> = {
  not_fixed: 'Not fixed yet',
  kept: 'Kept, not yet used',
  used: 'Used once',
  reused: 'Looked at again',
};

/**
 * The hold-out of a report (CF-06, D4/D5). Its numbers are shown only from a stored look (state used or reused, with a result):
 * before that the card says "kept" and offers the owner's unlock, and no figure of the block is read or shown.
 */
export function HoldOutCard({ runId, holdOut }: { runId: string; holdOut: ReportHoldOut }) {
  const [open, setOpen] = useState(false);
  const [jobId, setJobId] = useState<string | null>(null);
  const looked = (holdOut.state === 'used' || holdOut.state === 'reused') && holdOut.result !== null;
  return (
    <Box component="section" aria-label="Hold-out">
      <Typography variant="subtitle2">Hold-out</Typography>
      <Chip
        size="small"
        variant="outlined"
        color={holdOut.state === 'kept' ? 'success' : 'default'}
        label={stateWord[holdOut.state] ?? holdOut.state}
        sx={{ mr: 1 }}
      />
      <Typography component="span" variant="body2">
        {holdOut.text}
      </Typography>
      {holdOut.start !== null && (
        <Typography variant="body2">
          {`The block starts ${formatDate(holdOut.start)}${holdOut.lengthMonths === null ? '' : ` and is ${holdOut.lengthMonths} months long`}. ${holdOut.enforce ? 'Enforced: a result with no hold-out kept is capped at MARGINAL.' : 'Report only: nothing is capped.'}`}
        </Typography>
      )}
      {holdOut.state === 'kept' && (
        <>
          <Alert severity="info" sx={{ my: 1 }}>
            The numbers of the hold-out stay hidden until the owner unlocks it. Unlocking is irreversible: the frozen
            plan runs once over the block, the result is stored, and the block counts as seen.
          </Alert>
          {jobId !== null ? (
            <HoldOutJobStatus
              jobId={jobId}
              onDismiss={() => {
                setJobId(null);
              }}
            />
          ) : (
            <Button
              startIcon={<LockOpenIcon />}
              onClick={() => {
                setOpen(true);
              }}
            >
              Unlock the hold-out
            </Button>
          )}
          {open && (
            <UnlockDialog
              runId={runId}
              runReachesHoldOut={holdOut.runReachesHoldOut}
              onQueued={setJobId}
              onClose={() => {
                setOpen(false);
              }}
            />
          )}
        </>
      )}
      {looked && holdOut.result && <HoldOutNumbers result={holdOut.result} />}
    </Box>
  );
}

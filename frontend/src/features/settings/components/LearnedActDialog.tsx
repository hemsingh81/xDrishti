import { Typography } from '@mui/material';
import { Suspense } from 'react';
import { PasswordReasonDialog } from '@/features/execution';
import type { LearnedChangeView } from '@/shared/api/types';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useLearnedAct, type LearnedAct } from '../api/learnedApi';
import { refusalText } from '../lib/learnedWords';

const titles: Record<LearnedAct, string> = {
  approve: 'Approve this change?',
  undo: 'Undo this change?',
  dismiss: 'Dismiss this proposal?',
};
const confirmLabels: Record<LearnedAct, string> = { approve: 'Approve', undo: 'Undo', dismiss: 'Dismiss' };

/** Confirms one act on one change. A password is asked for only where the server says this act needs one. */
export function LearnedActDialog({
  change,
  act,
  onClose,
}: {
  change: LearnedChangeView;
  act: LearnedAct;
  onClose: () => void;
}) {
  const run = useLearnedAct();
  const needsPassword =
    (act === 'approve' && change.approveNeedsPassword) || (act === 'undo' && change.undoNeedsPassword);
  const what = `${change.label}: ${change.oldValue} to ${change.newValue}`;
  const close = () => {
    if (!run.isPending) onClose();
  };
  const send = (password?: string) => {
    run.mutate({ id: change.id, act, ...(password === undefined ? {} : { password }) }, { onSuccess: onClose });
  };

  if (needsPassword) {
    return (
      <Suspense fallback={null}>
        <PasswordReasonDialog
          open
          askReason={false}
          title={titles[act]}
          confirmLabel={confirmLabels[act]}
          intro={
            <Typography variant="body2">
              {what}. This is less cautious, so it asks for your password again, for this act only.
            </Typography>
          }
          pending={run.isPending}
          error={run.error}
          onClose={close}
          onSubmit={({ password }) => {
            send(password);
          }}
        />
      </Suspense>
    );
  }
  return (
    <ConfirmDialog
      open
      title={titles[act]}
      confirmLabel={confirmLabels[act]}
      busy={run.isPending}
      onCancel={close}
      onConfirm={() => {
        send();
      }}
    >
      {what}.
      {run.isError && (
        <Typography role="alert" color="error" variant="body2">
          {refusalText(run.error)}
        </Typography>
      )}
    </ConfirmDialog>
  );
}

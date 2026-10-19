import { Alert, Button, Checkbox, FormControlLabel, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import type { TradeBatch } from '@/shared/api';
import { capitalise } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { undoPreviewQuery } from '../../api/tradeQueries';
import { RetryAlert } from '../RetryAlert';
import { problemKind, problemText } from '../../lib/errors';
import { countOf, notRebuiltReason, replacedSentence } from '../../lib/tradeText';

interface UndoBatchDialogProps {
  batch: TradeBatch;
  /** The undo is running. */
  busy: boolean;
  /** Why the last attempt failed, if it did. */
  error: unknown;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Undo an import. It asks the API what the undo would do (a dry run) and says it before anything happens: the trades voided,
 * the holdings rebuilt and the lots replaced, the holdings that cannot be rebuilt — and what Undo cannot bring back. When
 * lots the owner dated by hand would be replaced, a tick is needed first.
 */
export function UndoBatchDialog({ batch, busy, error, onConfirm, onCancel }: UndoBatchDialogProps) {
  const preview = useQuery(undoPreviewQuery(batch.id));
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [confirmed, setConfirmed] = useState(false);
  const result = preview.data;
  const replaced = result ? replacedSentence(result.handDatedLotsReplaced, result.syncLotsReplaced) : null;
  const needsTick = (result?.handDatedLotsReplaced ?? 0) > 0;

  return (
    <AppDialog open onClose={onCancel} busy={busy} maxWidth="sm" initialFocusRef={cancelRef}>
      <AppDialogHeader subtitle={batch.fileName}>Undo this import?</AppDialogHeader>
      <AppDialogBody>
        {preview.isPending && <LoadingState label="Checking what the undo would do" />}
        {preview.isError && (
          <RetryAlert
            error={preview.error}
            fallback="What the undo would do could not be checked."
            onRetry={preview.refetch}
          />
        )}
        {result?.alreadyUndone && (
          <Alert severity="info">This import has been undone already. Nothing will change.</Alert>
        )}
        {result && !result.alreadyUndone && (
          <>
            <Typography gutterBottom>
              {`“${batch.fileName}” has ${countOf(batch.activeTrades, 'trade')} in ${countOf(batch.securities, 'security', 'securities')} that still count.`}
            </Typography>
            <Typography component="ul" sx={{ pl: 3, my: 1 }}>
              <li>
                {`${countOf(result.voided, 'trade')} will be voided: kept in the list, struck out, no longer counted. The file can be imported again.`}
              </li>
              <li>
                {result.holdingsRebuilt > 0
                  ? `${countOf(result.holdingsRebuilt, 'holding')} whose lots came from trades will be rebuilt from the trades that remain (${countOf(result.lotsReplaced, 'lot')} replaced).`
                  : 'No lot will be changed: no holding has lots that came from these trades.'}
              </li>
              {result.notRebuilt.length > 0 && (
                <li>
                  {`${countOf(result.notRebuilt.length, 'holding')} cannot be rebuilt and will keep ${result.notRebuilt.length === 1 ? 'its' : 'their'} lots: ${result.notRebuilt.map((entry) => `${entry.symbol} (${notRebuiltReason(entry.reason)})`).join(', ')}.`}
                </li>
              )}
            </Typography>
            {replaced && (
              <Alert severity="warning" sx={{ my: 1 }}>
                {`${capitalise(replaced)}, and they do not come back.`}
              </Alert>
            )}
          </>
        )}
        {result && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Undo cannot bring back lots that an earlier rebuild replaced, including lots you had dated by hand or a sync
            had added: a rebuilt holding goes back to the shape of the trades that remain, or to one lot of your
            broker’s quantity and cost.
          </Typography>
        )}
        {result && needsTick && !result.alreadyUndone && (
          <FormControlLabel
            sx={{ mt: 1 }}
            control={
              <Checkbox
                checked={confirmed}
                disabled={busy}
                onChange={(event) => {
                  setConfirmed(event.target.checked);
                }}
              />
            }
            label={`I understand that ${countOf(result.handDatedLotsReplaced, 'lot')} I dated by hand will be replaced and not come back.`}
          />
        )}
        {error ? (
          <Alert severity={problemKind(error) === 'sync_running' ? 'info' : 'error'} role="alert" sx={{ mt: 1.5 }}>
            {problemText(error, 'The import could not be undone. Nothing was changed.')}
          </Alert>
        ) : null}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onCancel} disabled={busy} ref={cancelRef}>
          {result?.alreadyUndone ? 'Done' : 'Cancel'}
        </Button>
        {!result?.alreadyUndone && (
          <Button
            variant="contained"
            color="error"
            disabled={!result || (needsTick && !confirmed)}
            loading={busy}
            onClick={onConfirm}
          >
            Undo import
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}

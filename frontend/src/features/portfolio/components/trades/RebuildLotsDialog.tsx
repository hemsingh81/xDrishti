import { Alert, Button, Checkbox, FormControlLabel } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import type { TradeRebuild } from '@/shared/api';
import { LoadingState } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useApplyRebuild } from '../../api/tradeMutations';
import { rebuildPreviewQuery } from '../../api/tradeQueries';
import { problemKind, problemText } from '../../lib/errors';
import { countOf, statusOf } from '../../lib/tradeText';
import { RebuildSummary } from './RebuildSummary';

interface RebuildLotsDialogProps {
  holdingId: string;
  symbol: string;
  onClose: () => void;
}

/**
 * Rebuild a holding's lots from its trades. The apply call carries no digest (unlike the import, whose preview is fingerprinted):
 * the API recomputes the rebuild when it is applied, and this dialog's counts come from the dry run made when it opened. A
 * holding that changed in between is answered with `applied: false` and the new status, which the dialog shows.
 * The dry run comes first and says what would change (nothing has changed yet);
 * the rebuild is applied on confirm — and, when hand-dated lots would be replaced, only after the owner ticks the box.
 */
export function RebuildLotsDialog({ holdingId, symbol, onClose }: RebuildLotsDialogProps) {
  const { notify } = useToast();
  const preview = useQuery(rebuildPreviewQuery(holdingId));
  const apply = useApplyRebuild(holdingId);
  const [confirmed, setConfirmed] = useState(false);
  const [refused, setRefused] = useState<TradeRebuild | null>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const rebuild = refused ?? preview.data?.rebuild ?? null;
  const ok = rebuild !== null && refused === null && statusOf(rebuild) === 'ok';
  const needsConfirmation = ok && rebuild.handDatedLotsReplaced > 0;

  function confirm() {
    apply.mutate(undefined, {
      onSuccess: (result) => {
        if (result.applied) {
          notify(
            `Rebuilt the lots of ${symbol}: ${countOf(result.rebuild.lotsBefore, 'lot')} replaced by ${countOf(result.rebuild.lotsAfter, 'lot')}.`,
          );
          onClose();
          return;
        }
        // The holding changed since the dry run and the rebuild no longer applies: show why, change nothing.
        setRefused(result.rebuild);
      },
    });
  }

  return (
    <AppDialog open onClose={onClose} busy={apply.isPending} maxWidth="sm" initialFocusRef={closeRef}>
      <AppDialogHeader subtitle="Nothing has changed yet: this is what the rebuild would do.">
        {`Rebuild the lots of ${symbol}`}
      </AppDialogHeader>
      <AppDialogBody>
        {preview.isPending && <LoadingState label="Checking the lots against the trades" />}
        {preview.isError && (
          <Alert
            severity="error"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  void preview.refetch();
                }}
              >
                Retry
              </Button>
            }
          >
            {problemText(preview.error, 'The rebuild could not be checked.')}
          </Alert>
        )}
        {rebuild && <RebuildSummary rebuild={rebuild} />}
        {refused && (
          <Alert severity="warning" sx={{ mt: 1.5 }}>
            The holding changed since you looked; the lots were not changed. Close this and open Rebuild lots again.
          </Alert>
        )}
        {needsConfirmation && (
          <FormControlLabel
            sx={{ mt: 1 }}
            control={
              <Checkbox
                checked={confirmed}
                disabled={apply.isPending}
                onChange={(event) => {
                  setConfirmed(event.target.checked);
                }}
              />
            }
            label={`I understand that ${countOf(rebuild.handDatedLotsReplaced, 'lot')} I dated by hand will be replaced.`}
          />
        )}
        {apply.isError && (
          <Alert
            severity={problemKind(apply.error) === 'sync_running' ? 'info' : 'error'}
            role="alert"
            sx={{ mt: 1.5 }}
          >
            {problemText(apply.error, 'The lots could not be rebuilt. Nothing was changed.')}
          </Alert>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={apply.isPending} ref={closeRef}>
          {ok ? 'Cancel' : 'Done'}
        </Button>
        {ok && (
          <Button
            variant="contained"
            disabled={needsConfirmation && !confirmed}
            loading={apply.isPending}
            onClick={confirm}
          >
            Rebuild lots
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}

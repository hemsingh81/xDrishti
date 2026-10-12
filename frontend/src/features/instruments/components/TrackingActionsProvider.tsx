import { useMemo, useRef, useState, type ReactNode } from 'react';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useTrackInstruments, useUntrackInstruments } from '../api/trackingMutations';
import { TrackingActionsContext, type TrackingActions } from '../hooks/trackingActionsContext';
import { trackingProblemText } from '../lib/errors';
import { trackedMessage, untrackedMessage } from '../lib/trackingText';

interface PendingUntrack {
  ids: readonly string[];
  label: string;
}

/**
 * Owns the track / untrack calls of the page: success and failure toasts, and the "Untrack?" confirmation (history is
 * kept, but collecting and scanning stop). Needs a ToastProvider above it.
 */
export function TrackingActionsProvider({ children }: { children: ReactNode }) {
  const { notify } = useToast();
  const { mutateAsync: trackCall, isPending: tracking } = useTrackInstruments();
  const { mutateAsync: untrackCall, isPending: untracking } = useUntrackInstruments();
  // The request stays while the dialog fades out, so its text does not change on the way.
  const [request, setRequest] = useState<PendingUntrack | null>(null);
  const [open, setOpen] = useState(false);
  const answer = useRef<((confirmed: boolean) => void) | null>(null);
  const busy = tracking || untracking;

  const actions = useMemo<TrackingActions>(() => {
    const track = async (ids: readonly string[], label: string) => {
      try {
        notify(trackedMessage(await trackCall(ids), label));
        return true;
      } catch (error) {
        notify(trackingProblemText(error, 'The instruments could not be tracked.'), 'error');
        return false;
      }
    };
    const untrack = async (ids: readonly string[], label: string) => {
      const confirmed = await new Promise<boolean>((resolve) => {
        // Only one question at a time: a call that is still waiting is answered "no", never left hanging.
        answer.current?.(false);
        answer.current = resolve;
        setRequest({ ids, label });
        setOpen(true);
      });
      if (!confirmed) return false;
      try {
        notify(untrackedMessage(await untrackCall(ids), label));
        return true;
      } catch (error) {
        notify(trackingProblemText(error, 'The instruments could not be untracked.'), 'error');
        return false;
      }
    };
    return { busy, track, untrack };
  }, [busy, notify, trackCall, untrackCall]);

  const close = (confirmed: boolean) => {
    answer.current?.(confirmed);
    answer.current = null;
    setOpen(false);
  };
  const count = request?.ids.length ?? 0;
  const many = count > 1;

  return (
    <TrackingActionsContext value={actions}>
      {children}
      <ConfirmDialog
        open={open}
        title={many ? `Untrack ${String(count)} instruments?` : 'Untrack this instrument?'}
        confirmLabel="Untrack"
        onCancel={() => {
          close(false);
        }}
        onConfirm={() => {
          close(true);
        }}
      >
        Collecting data and scans stop for {request?.label}. Existing history is kept and is reused if you track{' '}
        {many ? 'them' : 'it'} again.
      </ConfirmDialog>
    </TrackingActionsContext>
  );
}

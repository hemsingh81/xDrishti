import { Button, Tooltip } from '@mui/material';
import { stateAlpha } from '@/theme';
import { useTrackingActions } from '../hooks/trackingActionsContext';

interface TrackToggleProps {
  id: string;
  symbol: string;
  tracked: boolean;
  /** No longer in Dhan's list: it cannot be newly tracked. */
  retired: boolean;
}

/**
 * The Track / Untrack button of one instrument (row and drawer). While a call runs it is `aria-disabled` and does
 * nothing, so a button that has focus keeps it; a retired instrument that is not tracked is really disabled.
 */
export function TrackToggle({ id, symbol, tracked, retired }: TrackToggleProps) {
  const { busy, track, untrack } = useTrackingActions();
  const blocked = !tracked && retired;
  const verb = tracked ? 'Untrack' : 'Track';
  return (
    <Tooltip describeChild title={blocked ? "No longer in Dhan's list, so it cannot be tracked" : ''}>
      <span>
        <Button
          size="small"
          variant={tracked ? 'outlined' : 'contained'}
          disabled={blocked}
          aria-disabled={busy || undefined}
          aria-label={`${verb} ${symbol}`}
          sx={{ '&[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } }}
          onClick={() => {
            if (busy) return;
            void (tracked ? untrack([id], symbol) : track([id], symbol));
          }}
        >
          {verb}
        </Button>
      </span>
    </Tooltip>
  );
}

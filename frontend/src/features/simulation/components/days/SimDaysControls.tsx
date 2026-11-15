import { Alert, Button, FormControlLabel, Stack, Switch } from '@mui/material';
import { simProblemText, useSimDaysAction, type SimDays, type SimDaysAction } from '../../api/simDaysApi';

const live = (phase: string) => phase === 'idle' || phase === 'settling' || phase === 'settled';

/**
 * Run day (settle and play), Instant (settle and show the end of the day), Advance, Auto, Continue and Cancel, each enabled
 * only in the phases the API allows; a refusal (409) is shown in the API's words.
 */
export function SimDaysControls({
  run,
  onSettle,
  onInstantSettled,
}: {
  run: SimDays;
  /** A settle was asked: `end` for Instant, `play` for Run day. */
  onSettle: (show: 'play' | 'end') => void;
  /** Instant on a day already settled: show its end. */
  onInstantSettled: () => void;
}) {
  const action = useSimDaysAction(run.id);
  const busy = action.isPending;
  const { phase } = run;
  const manual = !run.auto && !busy;
  const go = (what: SimDaysAction, after?: () => void) => {
    action.mutate(what, { onSuccess: after });
  };
  return (
    <Stack sx={{ gap: 1.5 }}>
      <Stack
        direction="row"
        role="group"
        aria-label="Simulation controls"
        sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}
      >
        <Button
          variant="contained"
          disabled={!manual || phase !== 'idle'}
          onClick={() => {
            go('settle', () => {
              onSettle('play');
            });
          }}
          sx={{ minHeight: 44 }}
        >
          Run day
        </Button>
        <Button
          variant="outlined"
          disabled={!manual || (phase !== 'idle' && phase !== 'settled')}
          onClick={() => {
            if (phase === 'settled') onInstantSettled();
            else
              go('settle', () => {
                onSettle('end');
              });
          }}
          sx={{ minHeight: 44 }}
        >
          Instant
        </Button>
        <Button
          variant="outlined"
          disabled={!manual || phase !== 'settled'}
          onClick={() => {
            go('advance');
          }}
          sx={{ minHeight: 44 }}
        >
          Advance
        </Button>
        {phase === 'complete' && (
          <Button
            variant="contained"
            disabled={busy}
            onClick={() => {
              go('continue');
            }}
            sx={{ minHeight: 44 }}
          >
            Continue
          </Button>
        )}
        <FormControlLabel
          control={
            <Switch
              checked={run.auto}
              disabled={busy || !live(phase)}
              onChange={(e) => {
                go(e.target.checked ? 'auto-on' : 'auto-off');
              }}
            />
          }
          label="Auto"
          sx={{ minHeight: 44, ml: 0.5 }}
        />
        <Button
          color="error"
          disabled={busy || !live(phase)}
          onClick={() => {
            go('cancel');
          }}
          sx={{ minHeight: 44 }}
        >
          Cancel
        </Button>
      </Stack>
      {action.isError && (
        <Alert severity="error" role="alert">
          {simProblemText(action.error, 'The simulation refused the step.')}
        </Alert>
      )}
    </Stack>
  );
}

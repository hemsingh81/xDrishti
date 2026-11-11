import { Button, Stack, Tooltip, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { isStepUpCancelled } from '@/features/auth';
import { useToast } from '@/shared/ui/toastContext';
import { useFetchNow, useRecomputeNow } from '../api/systemMutations';
import { broadBenchmarkHintQuery, systemSettingsQuery } from '../api/systemQueries';
import { basketProblemText } from '../lib/errors';

export const benchmarkHint = 'Broad baskets compare better against Nifty 500: change in Portfolio';

/**
 * The owner-act buttons of the System group and the benchmark hint. "Fetch now" asks every source that is switched on;
 * "Recompute now" forms a version of every enabled derived basket. Both need the password step-up and are disabled, with
 * the reason, while their setting is OFF (the API answers 409 either way). The hint is text only and changes nothing.
 */
export function SystemTools() {
  const { notify } = useToast();
  const settings = useQuery(systemSettingsQuery);
  const hint = useQuery(broadBenchmarkHintQuery);
  const fetchNow = useFetchNow();
  const recompute = useRecomputeNow();

  const fetchOn = settings.data ? settings.data.fetchEnabled || settings.data.listFetchEnabled : false;
  const derivedOn = settings.data?.derivedEnabled ?? false;

  const run = (label: string, start: typeof fetchNow | typeof recompute) => () => {
    start.mutate(undefined, {
      onSuccess: (done) => {
        notify(`${label} finished: ${done.summary ?? 'done'}`);
      },
      onError: (error) => {
        if (!isStepUpCancelled(error)) notify(basketProblemText(error, `${label} failed.`), 'error');
      },
    });
  };

  return (
    <Stack spacing={1} sx={{ minWidth: 0 }}>
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <Tooltip title={fetchOn ? '' : 'Fetching is off in Settings'}>
          <span>
            <Button
              size="small"
              variant="outlined"
              disabled={!fetchOn || fetchNow.isPending}
              onClick={run('Fetch', fetchNow)}
            >
              Fetch now
            </Button>
          </span>
        </Tooltip>
        <Tooltip title={derivedOn ? '' : 'Recomputing is off in Settings'}>
          <span>
            <Button
              size="small"
              variant="outlined"
              disabled={!derivedOn || recompute.isPending}
              onClick={run('Recompute', recompute)}
            >
              Recompute now
            </Button>
          </span>
        </Tooltip>
        {settings.isError && (
          <Typography variant="body2" color="error">
            The fetch settings could not be read.
          </Typography>
        )}
      </Stack>
      {hint.data === true && (
        <Typography variant="body2" color="text.secondary">
          {benchmarkHint}
        </Typography>
      )}
    </Stack>
  );
}

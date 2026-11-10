import { Alert, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { Basket } from '@/shared/api';
import { sourceQuery } from '../api/systemQueries';
import { useLatestVersion } from '../hooks/useLatestVersion';
import { basketProblemText } from '../lib/errors';
import { asOfLine, failureReason, inSampleWarning } from '../lib/system';
import { SourceStatus } from './SourceStatus';

/** What a derived basket is not. The definition's own text is not served by the API yet, so this is the fixed statement. */
export const notText =
  'What this basket is NOT: a recommendation, a signal to trade, or a list that was known on past dates.';

/**
 * What the members card says about a stored system basket under its title: the source status (with the reason of a
 * rejection), the as-of line, and for a derived basket what it is not and the in-sample warning.
 */
export function SystemDetail({ basket }: { basket: Basket }) {
  const withFile = Boolean(basket.source);
  const source = useQuery({ ...sourceQuery(basket.id), enabled: withFile });
  const { derived, versions, latest } = useLatestVersion(basket);
  const line = asOfLine(basket, latest);
  return (
    <Stack spacing={0.75} sx={{ px: 2, pb: 1.5 }}>
      {withFile && <SourceStatus basket={basket} reason={failureReason(source.data)} />}
      {source.isError && (
        <Typography variant="body2" color="text.secondary">
          {basketProblemText(source.error, 'The source details could not be loaded.')}
        </Typography>
      )}
      {line && <Typography variant="body2">{line}</Typography>}
      {derived && versions.isError && (
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void versions.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          {basketProblemText(versions.error, 'The versions could not be loaded.')}
        </Alert>
      )}
      {derived && (
        <>
          <Typography variant="body2" color="text.secondary">
            {notText}
          </Typography>
          <Alert severity="warning" variant="outlined">
            {inSampleWarning}
          </Alert>
        </>
      )}
    </Stack>
  );
}

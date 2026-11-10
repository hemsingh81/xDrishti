import { Alert, Button, Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { useLatestVersion } from '../hooks/useLatestVersion';
import { basketProblemText } from '../lib/errors';
import { asOfLine, inSampleWarning } from '../lib/system';
import { SourceStatus } from './SourceStatus';

/**
 * What the members card says about a stored system basket under its title: the source status (with the reason of a
 * rejection), the as-of line, and for a derived basket what it is not and the in-sample warning.
 */
export function SystemDetail({ basket }: { basket: Basket }) {
  const { derived, versions, latest, definition } = useLatestVersion(basket);
  const line = asOfLine(basket, latest, definition?.name);
  return (
    <Stack spacing={0.75} sx={{ px: 2, pb: 1.5 }}>
      <SourceStatus basket={basket} />
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
          {definition && (
            <>
              <Typography variant="body2">{definition.description}</Typography>
              <Typography variant="body2" color="text.secondary">
                {`What this basket is NOT: ${definition.notThis}`}
              </Typography>
            </>
          )}
          <Alert severity="warning" variant="outlined">
            {inSampleWarning}
          </Alert>
        </>
      )}
    </Stack>
  );
}

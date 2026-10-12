import { Alert, Button, Chip, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { instrumentBasketsQuery } from '../api/basketsQueries';
import { basketProblemText } from '../lib/errors';
import { BasketDot } from './BasketDot';

/** The baskets that hold an instrument, as chips that open the basket on the Baskets screen (the Instruments drawer). */
export function InstrumentBaskets({ instrumentId }: { instrumentId: string }) {
  const query = useQuery(instrumentBasketsQuery(instrumentId));
  if (query.isPending) return <Typography color="text.secondary">Loading baskets…</Typography>;
  if (query.isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              void query.refetch();
            }}
          >
            Retry
          </Button>
        }
      >
        {basketProblemText(query.error, 'The baskets could not be loaded.')}
      </Alert>
    );
  }
  if (query.data.length === 0) return <Typography color="text.secondary">Not in any basket.</Typography>;
  return (
    <Stack
      component="ul"
      aria-label="Baskets of this instrument"
      direction="row"
      sx={{ m: 0, p: 0, gap: 0.75, flexWrap: 'wrap' }}
    >
      {query.data.map((basket) => (
        <li key={basket.basketId} style={{ listStyle: 'none' }}>
          <Chip
            size="small"
            variant="outlined"
            clickable
            component={RouterLink}
            to={`/baskets?basket=${basket.basketId}`}
            icon={<BasketDot colour={basket.colour} />}
            label={basket.name}
          />
        </li>
      ))}
    </Stack>
  );
}

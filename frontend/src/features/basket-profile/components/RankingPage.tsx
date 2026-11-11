import { Alert, Box, Button, Checkbox, FormControlLabel, FormGroup, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useUrlState } from '@/shared/analytics';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { basketListQuery, rankingQuery } from '../api/profileQueries';
import { RankingBoard } from './RankingBoard';
import { Section } from './Section';

const MIN = 2;
const MAX = 12;

/** `/baskets/ranking`: pick two to twelve baskets and see their relative strength side by side. The choice, the window and the index live in the address. */
export function RankingPage() {
  const url = useUrlState();
  const chosen = (url.text('basket') ?? '').split(',').filter((id) => id !== '');
  const params = { from: url.text('from'), to: url.text('to'), index: url.text('index') };
  const baskets = useQuery(basketListQuery);
  const ranking = useQuery(rankingQuery(chosen, params));

  function toggle(id: string) {
    const next = chosen.includes(id) ? chosen.filter((c) => c !== id) : [...chosen, id].slice(0, MAX);
    url.update({ basket: next.length > 0 ? next.join(',') : null });
  }

  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <PageHeader
        title="Baskets side by side"
        description="Relative strength of two to twelve baskets against the index. It describes the past and ranks nothing that cannot be told apart."
      />
      <Section
        title="Choose the baskets"
        note={`Pick ${String(MIN)} to ${String(MAX)}; ${String(chosen.length)} chosen.`}
      >
        {baskets.isPending ? (
          <LoadingState label="Loading the baskets…" />
        ) : baskets.isError ? (
          <ErrorState error={baskets.error} />
        ) : baskets.data.length === 0 ? (
          <Typography>There are no baskets yet.</Typography>
        ) : (
          <FormGroup
            aria-label="Baskets to compare"
            sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 220px), 1fr))' }}
          >
            {baskets.data.map((b) => (
              <FormControlLabel
                key={b.id}
                sx={{ minHeight: 44 }}
                control={
                  <Checkbox
                    checked={chosen.includes(b.id)}
                    disabled={!chosen.includes(b.id) && chosen.length >= MAX}
                    onChange={() => {
                      toggle(b.id);
                    }}
                  />
                }
                label={b.name}
              />
            ))}
          </FormGroup>
        )}
      </Section>
      {chosen.length < MIN ? (
        <Typography color="text.secondary">Choose at least two baskets to compare.</Typography>
      ) : ranking.isPending ? (
        <LoadingState label="Loading the ranking…" />
      ) : ranking.isError ? (
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void ranking.refetch();
              }}
              sx={{ minHeight: 44 }}
            >
              Try again
            </Button>
          }
        >
          {ranking.error instanceof ApiError || ranking.error instanceof Error
            ? ranking.error.message
            : 'The ranking could not be loaded.'}
        </Alert>
      ) : (
        <RankingBoard ranking={ranking.data} />
      )}
    </Box>
  );
}

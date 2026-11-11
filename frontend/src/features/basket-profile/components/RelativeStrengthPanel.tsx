import { Button, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { NonValue } from '@/shared/analytics';
import { TimeSeriesChart } from '@/shared/ui/chart';
import type { BasketProfile } from '../api/profileQueries';
import { fractionOf, levelOf } from '../lib/format';
import { FigureTable } from './FigureTable';
import { Section } from './Section';

/** The relative strength line (basket over index, 100 on the first day) and its returns; the rank among baskets is on the ranking board, not here. */
export function RelativeStrengthPanel({ profile }: { profile: BasketProfile }) {
  const { series, relativeStrength, basket } = profile;
  const any = series.relativeStrength.some((v) => typeof v === 'number');
  return (
    <Section
      title="Relative strength"
      note="The basket's level over the index's level, 100 on the first day. Above the start means it gained on the index."
    >
      {any ? (
        <TimeSeriesChart
          label="Relative strength against the index"
          series={[
            {
              id: 'rs',
              name: 'Relative strength',
              color: 'accent',
              data: series.dates.map((d, i) => [d, series.relativeStrength[i] ?? null]),
            },
          ]}
          valueFormatter={levelOf}
          height={220}
        />
      ) : (
        <NonValue
          state={{
            kind: 'unavailable',
            reason: relativeStrength[0]?.return.reason ?? 'Relative strength needs an index with daily bars.',
          }}
        />
      )}
      <FigureTable
        label="Relative strength returns"
        rows={relativeStrength.map((r) => ({
          id: `rs${String(r.window)}`,
          label: `Over ${String(r.window)} sessions`,
          view: r.return,
          format: fractionOf,
          tone: 'gain-loss' as const,
        }))}
      />
      <Typography variant="caption" color="text.secondary">
        The rank among baskets is on the ranking board.
      </Typography>
      <Button
        component={RouterLink}
        to={`/baskets/ranking?basket=${basket.id}`}
        size="small"
        sx={{ minHeight: 44, justifySelf: 'start' }}
      >
        Open the ranking board
      </Button>
    </Section>
  );
}

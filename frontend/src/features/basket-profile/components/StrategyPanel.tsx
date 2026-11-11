import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import {
  DataTable,
  NonValue,
  formatCount,
  formatRatio,
  formatRate,
  formatSigned,
  type Column,
} from '@/shared/analytics';
import { LoadingState } from '@/shared/ui';
import { regimeReasonQuery, strategyRowQuery, type BasketProfile } from '../api/profileQueries';
import { figure, nonValueOf } from '../lib/figure';
import { Section } from './Section';

type Cell = BasketProfile['holdOut']['cells'][number];

const verdictWords: Record<string, string> = {
  agrees: 'The two halves agree in sign',
  disagrees: 'The two halves disagree in sign',
  insufficient: 'Too few trades in a half',
  not_evaluable: 'A half has an expectancy of exactly zero',
};

const holdOutColumns: readonly Column<Cell>[] = [
  { kind: 'text', id: 'strategy', header: 'Strategy', render: (c) => c.strategy },
  {
    kind: 'metric',
    id: 'first',
    header: 'First half, expectancy (R)',
    metric: (c) => figure(c.firstExpectancy),
    format: formatSigned,
    n: (c) => c.firstN,
  },
  {
    kind: 'metric',
    id: 'second',
    header: 'Second half, expectancy (R)',
    metric: (c) => figure(c.secondExpectancy),
    format: formatSigned,
    n: (c) => c.secondN,
  },
  { kind: 'text', id: 'verdict', header: 'Result', render: (c) => verdictWords[c.verdict] ?? c.verdict },
];

/**
 * The strategy results of this basket: the row Reports already shows (read from its endpoint, not recomputed), the median split
 * of its trades, and a link into Reports. Basket by regime by strategy is shown disabled with the server's reason.
 */
export function StrategyPanel({ profile }: { profile: BasketProfile }) {
  const row = useQuery(strategyRowQuery(profile.basket.id));
  const reason = useQuery(regimeReasonQuery);
  const group = row.data?.groups[0];
  return (
    <>
      <Section
        title="Strategies on this basket"
        note="Trading results from the default run set, shown in Reports. They are not a hold return and are not set against the index."
      >
        {row.isPending ? (
          <LoadingState label="Loading the strategy results…" />
        ) : row.isError ? (
          <NonValue state={{ kind: 'error', reason: 'The strategy results could not be loaded.' }} />
        ) : group ? (
          <DataTable
            label="Strategy results for this basket"
            columns={[
              {
                kind: 'text',
                id: 'n',
                header: 'Trades',
                align: 'right',
                render: (g: typeof group) => formatCount(g.n),
              },
              { kind: 'metric', id: 'win', header: 'Win rate', metric: (g) => figure(g.winRate), format: formatRate },
              {
                kind: 'metric',
                id: 'exp',
                header: 'Expectancy (R)',
                metric: (g) => figure(g.expectancyR),
                format: formatSigned,
                tone: 'gain-loss',
              },
              {
                kind: 'metric',
                id: 'pf',
                header: 'Profit factor',
                metric: (g) => figure(g.profitFactor),
                format: formatRatio,
              },
            ]}
            rows={[group]}
            rowKey={(g) => g.key}
          />
        ) : (
          <NonValue state={{ kind: 'empty', reason: 'No closed trade of this basket in the default run set.' }} />
        )}
        <Button
          component={RouterLink}
          to={`/reports?basket=${profile.basket.id}`}
          size="small"
          sx={{ minHeight: 44, justifySelf: 'start' }}
        >
          Open in Reports
        </Button>
      </Section>
      <Section
        title="Stability across the two halves"
        note="The trades of each strategy split at the median exit date; the sign of the expectancy in each half is compared. It is a median split of stored trades, not the locked hold-out."
      >
        {profile.holdOut.cells.length === 0 ? (
          <NonValue state={nonValueOf(profile.holdOut.status)} />
        ) : (
          <DataTable
            label="Expectancy in each half"
            columns={holdOutColumns}
            rows={profile.holdOut.cells}
            rowKey={(c) => c.strategy}
          />
        )}
      </Section>
      <Section title="By market regime and strategy">
        <Box aria-disabled="true" sx={{ opacity: 0.85 }}>
          <NonValue
            state={{
              kind: 'unavailable',
              reason: reason.data ?? 'Trades carry no regime tag yet, so basket by regime by strategy cannot be shown.',
            }}
          />
        </Box>
        <Typography variant="caption" color="text.secondary">
          Needs the market regime recorded on each trade, which the simulation runs do not carry yet.
        </Typography>
      </Section>
    </>
  );
}

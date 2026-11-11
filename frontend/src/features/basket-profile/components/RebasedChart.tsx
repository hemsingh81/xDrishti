import { Typography } from '@mui/material';
import { NonValue } from '@/shared/analytics';
import { TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import type { BasketProfile } from '../api/profileQueries';
import { levelOf, percentOf } from '../lib/format';
import { FigureTable } from './FigureTable';
import { Section } from './Section';

const points = (dates: readonly string[], values: readonly (number | null)[]) =>
  dates.map((d, i) => [d, values[i] ?? null] as const);

/**
 * The basket against its index, both rebased to 100 on the first day, and the passive difference under it. The difference card
 * is the only place the difference to the index is printed: it is passive (hold against hold), never beside a trading result.
 */
export function RebasedChart({ profile }: { profile: BasketProfile }) {
  const { series, index, indexDifference, hold } = profile;
  if (series.dates.length === 0) {
    return (
      <Section title="Basket against the index">
        <NonValue
          state={{ kind: 'unavailable', reason: 'No index line: the basket has no day with enough members.' }}
        />
      </Section>
    );
  }
  const lines: ChartSeries[] = [
    { id: 'basket', name: profile.basket.name, color: 'accent', data: points(series.dates, series.basket) },
    ...(index.symbol
      ? [
          {
            id: 'index',
            name: index.symbol,
            color: 'neutral',
            dash: 'dashed',
            data: points(series.dates, series.index),
          } satisfies ChartSeries,
        ]
      : []),
  ];
  const gap: ChartSeries = {
    id: 'gap',
    name: 'Basket minus index (points)',
    color: 'info',
    data: series.dates.map((d, i) => {
      const other = series.index[i];
      const mine = series.basket[i];
      return [d, typeof other === 'number' && typeof mine === 'number' ? mine - other : null] as const;
    }),
  };
  return (
    <Section
      title="Basket against the index"
      note="Equal weight, rebalanced daily, price return. Both lines start at 100."
    >
      <TimeSeriesChart label="Basket and index rebased to 100" series={lines} valueFormatter={levelOf} height={260} />
      {index.symbol && series.index.some((v) => typeof v === 'number') && (
        <TimeSeriesChart
          label="Basket minus index, in index points"
          series={[gap]}
          valueFormatter={levelOf}
          height={220}
        />
      )}
      <FigureTable
        label="Passive return against the index"
        rows={[
          {
            id: 'hold',
            label: 'Basket, bought at the start and held',
            view: hold,
            format: percentOf,
            tone: 'gain-loss',
          },
          {
            id: 'index',
            label: 'Index over the same dates',
            view: indexDifference.index,
            format: percentOf,
            tone: 'gain-loss',
          },
          {
            id: 'difference',
            label: 'Difference to the index (passive)',
            view: indexDifference.difference,
            format: percentOf,
            tone: 'gain-loss',
          },
        ]}
      />
      <Typography variant="caption" color="text.secondary">
        A hold is not a trading result; the difference says what the basket did, not what a strategy would.
      </Typography>
    </Section>
  );
}

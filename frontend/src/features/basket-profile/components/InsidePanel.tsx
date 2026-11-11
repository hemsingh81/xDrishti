import { Typography } from '@mui/material';
import { TimeSeriesChart } from '@/shared/ui/chart';
import type { BasketProfile } from '../api/profileQueries';
import { fractionOf, ratioOf } from '../lib/format';
import type { Figure } from '../lib/figure';
import { FigureTable, type FigureRow } from './FigureTable';
import { Section } from './Section';

type Share = BasketProfile['breadth']['b20'];

const shareRow = (id: string, label: string, s: Share): FigureRow => ({
  id,
  label: s.voters > 0 ? `${label} (${String(s.voters)} members counted)` : label,
  view: s.share,
  format: fractionOf,
});

const plain = (id: string, label: string, view: Figure, format: (v: number) => string): FigureRow => ({
  id,
  label,
  view,
  format,
});

/** What is going on inside the basket on the last session with a bar: breadth, dispersion and how alike the members move. */
export function InsidePanel({ profile }: { profile: BasketProfile }) {
  const { breadth, dispersion, correlation } = profile;
  const series = breadth.seriesDates.map((d, i) => [d, breadth.seriesB20[i] ?? null] as const);
  const hasSeries = series.some((p) => typeof p[1] === 'number');
  return (
    <Section
      title="Inside the basket"
      note="Shares of members; a member without enough history is left out of that figure and counted. Under the minimum number of voters there is no figure."
    >
      <FigureTable
        label="Breadth and dispersion"
        rows={[
          shareRow('b20', 'Above their 20-day average', breadth.b20),
          shareRow('b50', 'Above their 50-day average', breadth.b50),
          shareRow('b200', 'Above their 200-day average', breadth.b200),
          shareRow('ad', 'Advancers over advancers plus decliners', breadth.adShare),
          shareRow('high', 'At their 252-session high', breadth.newHighShare),
          plain('dispToday', 'Spread of member returns today (standard deviation)', dispersion.today, fractionOf),
          plain('dispMean', 'The same, mean of the last 20 sessions', dispersion.mean20, fractionOf),
          plain('dispIqr', 'Interquartile range of member returns today', dispersion.iqr, fractionOf),
          plain('rho', 'Average correlation between members', correlation.rhoBar, ratioOf),
          plain('neff', 'Effective number of independent bets', correlation.nEff, ratioOf),
        ]}
      />
      {hasSeries && (
        <TimeSeriesChart
          label="Share of members above their 20-day average"
          series={[{ id: 'b20', name: 'Above the 20-day average', color: 'accent', data: series }]}
          valueFormatter={fractionOf}
          height={220}
        />
      )}
      <Typography variant="caption" color="text.secondary">
        The effective number of bets is a heuristic from the average correlation, not a measured count.
        {correlation.sampled
          ? ' The basket was larger than the most the correlation reads, so the most liquid members were used.'
          : ''}
      </Typography>
    </Section>
  );
}

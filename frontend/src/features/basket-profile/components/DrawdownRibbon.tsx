import { Typography } from '@mui/material';
import { NonValue } from '@/shared/analytics';
import { TimeSeriesChart } from '@/shared/ui/chart';
import type { BasketProfile } from '../api/profileQueries';
import { fractionOf } from '../lib/format';
import { Section } from './Section';

/** The fall from the running peak, day by day, with the state of the drawdown now in words. */
export function DrawdownRibbon({ profile }: { profile: BasketProfile }) {
  const { series, risk } = profile;
  if (series.dates.length === 0 || risk.mdd.state !== 'value') {
    return (
      <Section title="Drawdown">
        <NonValue
          state={{ kind: 'unavailable', reason: risk.mdd.reason ?? 'There is no index line to take a drawdown from.' }}
        />
      </Section>
    );
  }
  return (
    <Section title="Drawdown" note="Fall from the highest level so far, as a share of that level.">
      <TimeSeriesChart
        label="Drawdown of the basket index"
        series={[
          {
            id: 'drawdown',
            name: 'Drawdown',
            color: 'bad',
            data: series.dates.map((d, i) => [d, series.drawdown[i] ?? null]),
          },
        ]}
        valueFormatter={fractionOf}
        height={220}
      />
      <Typography variant="body2" color="text.secondary">
        {risk.drawdownOpen
          ? `Below its peak now, for ${String(risk.daysSincePeak)} sessions.`
          : 'At a peak on the last session.'}
      </Typography>
    </Section>
  );
}

import { Typography } from '@mui/material';
import { BarList, NonValue, type BarRow } from '@/shared/analytics';
import type { BasketProfile } from '../api/profileQueries';
import { figure, nonValueOf } from '../lib/figure';
import { fractionOf, percentOf } from '../lib/format';
import { Fig } from './Fig';
import { Section } from './Section';

type Row = BasketProfile['contribution']['top'][number];

const bars = (rows: readonly Row[]): BarRow[] =>
  rows.map((r) => ({ id: r.symbol, label: r.symbol, state: { kind: 'value', value: r.returnPct } }));

/** Who made the return: the five largest and five smallest members by their own hold return, and the share of the three largest. */
export function ContributionBars({ profile }: { profile: BasketProfile }) {
  const { contribution } = profile;
  const share = figure(contribution.top3Share);
  if (share.kind !== 'value' || contribution.top.length === 0) {
    return (
      <Section title="Contribution to the return">
        <NonValue state={nonValueOf(contribution.top3Share)} />
      </Section>
    );
  }
  return (
    <Section
      title="Contribution to the return"
      note="Each member's own hold return in percent; the shares add up to the basket's hold return."
    >
      <Typography>
        {'The three largest contributors make '}
        <Fig view={contribution.top3Share} format={fractionOf} />
        {` of the return (${String(contribution.included)} members counted).`}
      </Typography>
      <Typography variant="subtitle2" component="h3">
        Largest
      </Typography>
      <BarList label="Largest contributors" rows={bars(contribution.top)} format={percentOf} />
      <Typography variant="subtitle2" component="h3">
        Smallest
      </Typography>
      <BarList label="Smallest contributors" rows={bars(contribution.bottom)} format={percentOf} />
    </Section>
  );
}

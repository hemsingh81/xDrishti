import { NonValue, DataTable, type Column } from '@/shared/analytics';
import type { BasketProfile } from '../api/profileQueries';
import { figure, nonValueOf } from '../lib/figure';
import { fractionOf } from '../lib/format';
import { Section } from './Section';

type Row = BasketProfile['regime']['rows'][number];

const columns: readonly Column<Row>[] = [
  {
    kind: 'text',
    id: 'regime',
    header: 'Regime of the previous session',
    render: (r) => r.label.replaceAll('_', ' ').toLowerCase(),
  },
  { kind: 'text', id: 'days', header: 'Days', align: 'right', render: (r) => String(r.days) },
  {
    kind: 'metric',
    id: 'mean',
    header: 'Mean daily return',
    metric: (r) => figure(r.meanReturn),
    format: fractionOf,
    tone: 'gain-loss',
  },
  { kind: 'metric', id: 'vol', header: 'Volatility (annualised)', metric: (r) => figure(r.vol), format: fractionOf },
  {
    kind: 'metric',
    id: 'hit',
    header: 'Days ahead of the index',
    metric: (r) => figure(r.hitRate),
    format: fractionOf,
  },
];

/** How the basket index behaved on days that followed each market regime. A regime with too few days shows its state; no regime recorded is a reason, not a chart. */
export function RegimePanel({ profile }: { profile: BasketProfile }) {
  const { regime } = profile;
  if (regime.rows.length === 0) {
    return (
      <Section title="By market regime">
        <NonValue state={nonValueOf(regime.status)} />
      </Section>
    );
  }
  return (
    <Section
      title="By market regime"
      note={`The regime of the session before each day, so no day is judged by its own close. A regime needs ${String(regime.minDays)} days for a figure; ${String(regime.unknown)} days had no regime recorded and are in no row.`}
    >
      <DataTable label="Index by market regime" columns={columns} rows={regime.rows} rowKey={(r) => String(r.code)} />
    </Section>
  );
}

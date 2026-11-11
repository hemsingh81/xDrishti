import type { BasketProfile } from '../api/profileQueries';
import { fractionOf, ratioOf, sessionsOf } from '../lib/format';
import { FigureTable, type FigureRow } from './FigureTable';
import { Section } from './Section';

/** The risk figures of the basket index. Beta, intercept, tracking error and the information ratio are the 252-day window against the chosen index. */
export function RiskTable({ profile }: { profile: BasketProfile }) {
  const { risk } = profile;
  const rows: FigureRow[] = [
    { id: 'vol20', label: 'Volatility, 20 sessions (annualised)', view: risk.vol20, format: fractionOf },
    { id: 'vol60', label: 'Volatility, 60 sessions (annualised)', view: risk.vol60, format: fractionOf },
    { id: 'vol252', label: 'Volatility, 252 sessions (annualised)', view: risk.vol252, format: fractionOf },
    { id: 'mdd', label: 'Deepest fall from a peak', view: risk.mdd, format: fractionOf, tone: 'gain-loss' },
    { id: 'mddDuration', label: 'Longest time below a peak', view: risk.mddDuration, format: sessionsOf },
    { id: 'beta60', label: 'Beta against the index, 60 sessions', view: risk.beta60, format: ratioOf },
    { id: 'beta252', label: 'Beta against the index, 252 sessions', view: risk.beta252, format: ratioOf },
    { id: 'alpha', label: 'Intercept vs index, 252 sessions (annualised)', view: risk.alpha, format: fractionOf },
    { id: 'te', label: 'Tracking error, 252 sessions', view: risk.trackingError, format: fractionOf },
    { id: 'ir', label: 'Information ratio, 252 sessions', view: risk.informationRatio, format: ratioOf },
    { id: 'up', label: 'Up capture', view: risk.upCapture, format: ratioOf },
    { id: 'down', label: 'Down capture', view: risk.downCapture, format: ratioOf },
  ];
  return (
    <Section
      title="Risk"
      note="Of the basket index; risk-free rate ignored. A figure with too few days says so and shows no number."
    >
      <FigureTable label="Risk figures" rows={rows} />
    </Section>
  );
}

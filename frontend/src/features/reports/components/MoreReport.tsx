import type { ReactNode } from 'react';
import type { MoreView, ReportPanelProps } from '../lib/reportFilter';
import { CalendarReport } from './CalendarReport';
import { DistributionReport } from './DistributionReport';
import { ExitComparison } from './ExitComparison';
import { InstrumentScorecard } from './InstrumentScorecard';
import { MaeMfeReport } from './MaeMfeReport';
import { MonteCarloReport } from './MonteCarloReport';
import { SectorReport } from './SectorReport';
import { SideReport } from './SideReport';
import { StrategiesReport } from './StrategiesReport';

const reports: Record<MoreView, (props: ReportPanelProps) => ReactNode> = {
  strategies: (p) => <StrategiesReport {...p} />,
  side: (p) => <SideReport {...p} />,
  calendar: (p) => <CalendarReport {...p} />,
  maemfe: (p) => <MaeMfeReport {...p} />,
  distribution: (p) => <DistributionReport {...p} />,
  instruments: (p) => <InstrumentScorecard {...p} />,
  sectors: (p) => <SectorReport {...p} />,
  exits: (p) => <ExitComparison {...p} />,
  montecarlo: () => <MonteCarloReport />,
};

/** The P8-10 report of a view. */
export function MoreReport({ view, ...props }: ReportPanelProps & { view: MoreView }) {
  return <>{reports[view](props)}</>;
}

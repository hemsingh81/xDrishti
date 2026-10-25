import type { ReactNode } from 'react';
import type { ReportPanelProps, ReportView } from '../lib/reportFilter';
import { CalendarReport } from './CalendarReport';
import { DistributionReport } from './DistributionReport';
import { InstrumentScorecard } from './InstrumentScorecard';
import { MaeMfeReport } from './MaeMfeReport';
import { SectorReport } from './SectorReport';
import { SideReport } from './SideReport';
import { StrategiesReport } from './StrategiesReport';

const reports: Partial<Record<ReportView, (props: ReportPanelProps) => ReactNode>> = {
  strategies: (p) => <StrategiesReport {...p} />,
  side: (p) => <SideReport {...p} />,
  calendar: (p) => <CalendarReport {...p} />,
  maemfe: (p) => <MaeMfeReport {...p} />,
  distribution: (p) => <DistributionReport {...p} />,
  instruments: (p) => <InstrumentScorecard {...p} />,
  sectors: (p) => <SectorReport {...p} />,
};

/** The P8-10 report of a view. */
export function MoreReport({ view, ...props }: ReportPanelProps & { view: ReportView }) {
  return <>{reports[view]?.(props)}</>;
}

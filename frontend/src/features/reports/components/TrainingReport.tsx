import { formatR, formatRate } from '@/shared/analytics';
import type { ReportQuery } from '../lib/reportFilter';
import type { TrainingReportType } from '../lib/trainingReports';
import { DistributionPanel } from './DistributionPanel';
import { EquityPanel } from './EquityPanel';
import { GroupBars } from './GroupBars';
import { KpiTiles } from './KpiTiles';

interface TrainingReportProps {
  type: TrainingReportType;
  /** Carries `purpose=training`, the stock or basket and the range. */
  query: ReportQuery;
}

/** One report over a training simulation job. The same panels as the Reports Overview, so the same states. */
export function TrainingReport({ type, query }: TrainingReportProps) {
  const panel = { query, min: null };
  switch (type) {
    case 'key-figures':
      return <KpiTiles {...panel} />;
    case 'equity':
      return <EquityPanel {...panel} />;
    case 'monthly':
      return <GroupBars {...panel} title="Monthly total R" lens="month" figure={(g) => g.totalR} format={formatR} />;
    case 'weekday':
      return (
        <GroupBars
          {...panel}
          title="Win rate by weekday"
          lens="weekday"
          figure={(g) => g.winRate}
          format={formatRate}
          diverging={false}
        />
      );
    case 'distribution':
      return <DistributionPanel {...panel} />;
  }
}

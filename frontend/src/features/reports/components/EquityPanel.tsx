import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { carriesNumber, EvidenceBlock, formatR, NonValue } from '@/shared/analytics';
import { TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import { equityQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { asNonValue, metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

/** Running R and the drawdown under it, one point per exit date (the chart carries its own table and summary). */
export function EquityPanel({ query, min }: ReportPanelProps) {
  const result = useQuery(equityQuery(query));
  return (
    <QueryPanel title="Equity and drawdown" query={result}>
      {({ series, evidence }) => {
        const all = series[0];
        const points = all?.points ?? [];
        const total = all ? metricFromApi(all.totalR, all.n, min) : ({ kind: 'empty' } as const);
        // No curve and no numbers unless the total itself is a value (or provisional): a thin group has only a reason.
        if (!all || !carriesNumber(total) || points.length === 0) {
          return <NonValue state={asNonValue(total.kind === 'value' ? { kind: 'empty' } : total)} />;
        }
        const chartSeries: ChartSeries[] = [
          { id: 'equity', name: 'Running R', color: 'accent', data: points.map((p) => [p.date, p.cumulativeR]) },
          {
            id: 'drawdown',
            name: 'Drawdown',
            color: 'bad',
            dash: 'dashed',
            data: points.map((p) => [p.date, -p.drawdownR]),
          },
        ];
        const deepest = Math.max(...points.map((p) => p.drawdownR));
        const last = points[points.length - 1];
        return (
          <Box sx={{ display: 'grid', gap: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {`${String(points.length)} exit dates, ${String(all.n)} trades; ends at ${formatR(last?.cumulativeR)}; deepest drawdown ${formatR(-deepest)}. One point per exit date.`}
            </Typography>
            <TimeSeriesChart
              label="Equity and drawdown in R"
              series={chartSeries}
              valueFormatter={formatR}
              height={260}
            />
            <EvidenceBlock {...evidenceOf(evidence, { n: all.n })} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

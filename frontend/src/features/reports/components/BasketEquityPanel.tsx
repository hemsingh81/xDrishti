import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { carriesNumber, EvidenceBlock, formatR } from '@/shared/analytics';
import { TimeSeriesChart, type ChartColor, type ChartDash, type ChartSeries } from '@/shared/ui/chart';
import { equityQuery } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

// Six colour roles with three dash styles: the lines differ by more than colour (P8-09 decision 2).
const looks: readonly { color: ChartColor; dash: ChartDash }[] = [
  { color: 'accent', dash: 'solid' },
  { color: 'info', dash: 'dashed' },
  { color: 'good', dash: 'dotted' },
  { color: 'warn', dash: 'solid' },
  { color: 'bad', dash: 'dashed' },
  { color: 'neutral', dash: 'dotted' },
];

/** Cumulative R of every compared basket, one point per exit date. Curves overlap by design; a basket without a curve is named. */
export function BasketEquityPanel({ query, min }: ReportPanelProps) {
  const result = useQuery(equityQuery(query, 'basket'));
  const names = useBasketNames();
  return (
    <QueryPanel title="Cumulative R by basket" query={result}>
      {({ series, evidence, nonAdditive }) => {
        const drawn: ChartSeries[] = [];
        const absent: string[] = [];
        series.forEach((s) => {
          const name = names.part('basket', s.parts[0]?.value ?? null);
          if (s.points.length === 0 || !carriesNumber(metricFromApi(s.totalR, s.n, min))) {
            absent.push(name);
            return;
          }
          const look = looks[drawn.length % looks.length];
          drawn.push({
            id: s.key,
            name,
            color: look?.color ?? 'accent',
            dash: look?.dash,
            data: s.points.map((p) => [p.date, p.cumulativeR]),
          });
        });
        return (
          <Box sx={{ display: 'grid', gap: 1 }}>
            {drawn.length > 0 && (
              <TimeSeriesChart
                label="Cumulative R by basket (the curves overlap: shared instruments)"
                series={drawn}
                valueFormatter={formatR}
                height={260}
              />
            )}
            {absent.length > 0 && (
              <Typography variant="body2" color="text.secondary">
                {`No curve for ${absent.join(', ')}: no trade, or too few for a figure.`}
              </Typography>
            )}
            {nonAdditive && (
              <Typography variant="body2" color="text.secondary">
                {`${NON_ADDITIVE_NOTE} The curves overlap.`}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

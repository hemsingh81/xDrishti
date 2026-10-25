import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, EvidenceBlock, formatCount, formatR, formatRate, formatRatio } from '@/shared/analytics';
import { groupsQuery, type GroupView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { EquityByGroup } from './EquityByGroup';
import { QueryPanel } from './Panel';

const sideName = (value: string | null | undefined) =>
  value === 'BUY' ? 'Long (BUY)' : value === 'SELL' ? 'Short (SELL)' : 'Not recorded';

/** R6: long against short, figure by figure, and running R of each side. */
export function SideReport({ query, min }: ReportPanelProps) {
  const result = useQuery(groupsQuery(query, 'side'));
  const figure = (
    id: string,
    header: string,
    pick: (g: GroupView) => GroupView['totalR'],
    format: (v: number) => string,
    gain = false,
  ) => ({
    kind: 'metric' as const,
    id,
    header,
    metric: (g: GroupView) => metricFromApi(pick(g), g.n, min),
    format,
    tone: gain ? ('gain-loss' as const) : undefined,
  });
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <QueryPanel title="Long against short" query={result}>
        {({ groups, evidence }) => (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            <DataTable
              label="Long against short"
              rows={groups}
              rowKey={(g) => g.key}
              columns={[
                { kind: 'text', id: 'side', header: 'Side', render: (g) => sideName(g.parts[0]?.value) },
                { kind: 'text', id: 'n', header: 'n', align: 'right', render: (g) => formatCount(g.n) },
                figure('win', 'Win rate', (g) => g.winRate, formatRate),
                figure('exp', 'Expectancy', (g) => g.expectancyR, formatR, true),
                figure('total', 'Total R', (g) => g.totalR, formatR, true),
                figure('pf', 'Profit factor', (g) => g.profitFactor, formatRatio),
                figure('dd', 'Max drawdown', (g) => g.maxDrawdownR, formatR),
              ]}
            />
            <Typography variant="body2" color="text.secondary">
              Descriptive, not a ranking: one side can look better by chance over a thin sample.
            </Typography>
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        )}
      </QueryPanel>
      <EquityByGroup query={query} min={min} title="Cumulative R by side" lens="side" />
    </Box>
  );
}

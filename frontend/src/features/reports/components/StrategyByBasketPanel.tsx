import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { BarList, formatR, NonValue, type BarRow } from '@/shared/analytics';
import { whatWorksQuery, type GroupView } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportQuery } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

/** Expectancy by strategy: one bar list per strategy, one row per basket; a thin cell shows its state, not a bar. */
export function StrategyByBasketPanel({ query }: { query: ReportQuery }) {
  const result = useQuery(whatWorksQuery(query, 'basket,strategy'));
  const names = useBasketNames();
  return (
    <QueryPanel title="Expectancy by strategy per basket" query={result}>
      {(w) => {
        const cells: GroupView[] = [...w.ranked.map((r) => r.group), ...w.notRanked.map((r) => r.group)];
        const strategies = [...new Set(cells.map((g) => g.parts.find((p) => p.dimension === 'strategy')?.value ?? ''))];
        if (strategies.length === 0)
          return <NonValue state={{ kind: 'empty', reason: 'No trades in this selection.' }} />;
        return (
          <Box sx={{ display: 'grid', gap: 2 }}>
            {strategies.map((strategy) => {
              const rows: BarRow[] = cells
                .filter((g) => (g.parts.find((p) => p.dimension === 'strategy')?.value ?? '') === strategy)
                .map((g) => ({
                  id: g.key,
                  label: names.part('basket', g.parts.find((p) => p.dimension === 'basket')?.value ?? null),
                  state: metricFromApi(g.expectancyR, g.n, w.minTrades),
                  n: g.n,
                }));
              return (
                <Box key={strategy}>
                  <Typography variant="h3" component="h3" sx={{ fontSize: 'body1.fontSize', mb: 1 }}>
                    {strategy || 'Not recorded'}
                  </Typography>
                  <BarList
                    label={`Expectancy of ${strategy || 'not recorded'} per basket`}
                    rows={rows}
                    format={formatR}
                  />
                </Box>
              );
            })}
          </Box>
        );
      }}
    </QueryPanel>
  );
}

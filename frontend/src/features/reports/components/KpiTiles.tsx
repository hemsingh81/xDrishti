import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount, formatRate, formatRatio, formatR, MetricTile, type MetricState } from '@/shared/analytics';
import { summaryQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

interface Tile {
  label: string;
  state: MetricState;
  format: (value: number) => string;
  gainLoss?: boolean;
}

/** The R-based headline figures of the whole population (no rupee figure: the engine has no sized trades). */
export function KpiTiles({ query, min }: ReportPanelProps) {
  const result = useQuery(summaryQuery(query));
  return (
    <QueryPanel title="Key figures" query={result}>
      {({ all: g, evidence }) => {
        const tiles: Tile[] = [
          {
            label: 'Trades',
            state: g.n > 0 ? { kind: 'value', value: g.n } : { kind: 'empty', reason: 'No closed trades.' },
            format: formatCount,
          },
          { label: 'Win rate', state: metricFromApi(g.winRate, g.n, min), format: formatRate },
          { label: 'Expectancy', state: metricFromApi(g.expectancyR, g.n, min), format: formatR, gainLoss: true },
          { label: 'Total R', state: metricFromApi(g.totalR, g.n, min), format: formatR, gainLoss: true },
          { label: 'Profit factor', state: metricFromApi(g.profitFactor, g.n, min), format: formatRatio },
          { label: 'Max drawdown', state: metricFromApi(g.maxDrawdownR, g.n, min), format: formatR },
          { label: 'Longest loss streak', state: metricFromApi(g.longestLossStreak, g.n, min), format: formatCount },
        ];
        return (
          <Box
            sx={{
              display: 'grid',
              gap: 1.5,
              gridTemplateColumns: {
                // Two columns on a phone, one when enlarged text would make a tile narrower than 9rem (the figures stay whole at 200%).
                xs: 'repeat(auto-fit, minmax(min(100%, max(9rem, 45%)), 1fr))',
                md: 'repeat(4, minmax(0, 1fr))',
                xl: 'repeat(7, minmax(0, 1fr))',
              },
            }}
          >
            {tiles.map((tile) => (
              <MetricTile
                key={tile.label}
                label={tile.label}
                state={tile.state}
                format={tile.format}
                tone={tile.gainLoss ? 'gain-loss' : 'neutral'}
                evidence={evidenceOf(evidence, { n: g.n })}
              />
            ))}
          </Box>
        );
      }}
    </QueryPanel>
  );
}

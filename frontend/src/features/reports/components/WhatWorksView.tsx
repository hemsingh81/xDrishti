import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, EvidenceBlock, formatCount, formatR, formatRate, NonValue } from '@/shared/analytics';
import { whatWorksQuery, type GroupView } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportQuery } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

/** The full answer: ranked cells by the lower bound of expectancy, then every other cell with its reason (never hidden). */
export function WhatWorksView({ query }: { query: ReportQuery }) {
  const result = useQuery(whatWorksQuery(query));
  const baskets = useBasketNames();
  const cellName = (group: GroupView) =>
    group.parts.map((part) => `${part.dimension} ${baskets.part(part.dimension, part.value)}`).join(' · ');
  return (
    <QueryPanel title="Ranked cells" query={result}>
      {(w) => {
        const min = w.minTrades;
        return (
          <Box sx={{ display: 'grid', gap: 2 }}>
            <Typography variant="body1" sx={{ fontWeight: 600 }}>
              {w.headline}
            </Typography>
            <Typography variant="body2">
              {`Looked at ${formatCount(w.cellsLookedAt)} cells, compared ${formatCount(w.cellsComparable)}. ${w.selectionNote} About ${w.expectedChancePositives.toFixed(1)} positive lower bounds are expected by chance; ${formatCount(w.positiveLowerBound)} found.`}
            </Typography>
            {w.unavailableDimensions.length > 0 && (
              <Box component="ul" aria-label="Dimensions left out" sx={{ m: 0, pl: 2.5 }}>
                {w.unavailableDimensions.map((text) => (
                  <li key={text}>
                    <Typography variant="body2">{text}</Typography>
                  </li>
                ))}
              </Box>
            )}
            {w.ranked.length === 0 ? (
              <NonValue state={{ kind: 'empty', reason: 'No cell is ranked.' }} />
            ) : (
              <DataTable
                label="Ranked cells"
                rows={w.ranked}
                rowKey={(row) => row.group.key}
                columns={[
                  { kind: 'text', id: 'rank', header: 'Rank', render: (row) => formatCount(row.rank) },
                  { kind: 'text', id: 'cell', header: 'Cell', render: (row) => cellName(row.group) },
                  { kind: 'text', id: 'n', header: 'n', align: 'right', render: (row) => formatCount(row.group.n) },
                  {
                    kind: 'metric',
                    id: 'lower',
                    header: 'Expectancy lower bound',
                    metric: (row) => metricFromApi(row.group.expectancyLowerR, row.group.n, min),
                    format: formatR,
                    tone: 'gain-loss',
                  },
                  {
                    kind: 'metric',
                    id: 'shrunk',
                    header: 'Shrunk expectancy',
                    metric: (row) => metricFromApi(row.group.shrunkExpectancyR, row.group.n, min),
                    format: formatR,
                    tone: 'gain-loss',
                  },
                  {
                    kind: 'metric',
                    id: 'winlower',
                    header: 'Win rate lower bound',
                    metric: (row) => metricFromApi(row.group.winRateLower, row.group.n, min),
                    format: formatRate,
                  },
                ]}
              />
            )}
            <Typography variant="h3" component="h3" sx={{ fontSize: 'body1.fontSize' }}>
              Not ranked
            </Typography>
            <DataTable
              label="Cells not ranked"
              rows={w.notRanked}
              rowKey={(row) => row.group.key}
              columns={[
                { kind: 'text', id: 'cell', header: 'Cell', render: (row) => cellName(row.group) },
                { kind: 'text', id: 'n', header: 'n', align: 'right', render: (row) => formatCount(row.group.n) },
                { kind: 'text', id: 'reason', header: 'Why not ranked', render: (row) => row.reason },
              ]}
            />
            {w.nonAdditive && (
              <Typography variant="body2" color="text.secondary">
                {NON_ADDITIVE_NOTE}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(w.evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, EvidenceBlock, formatCount, formatR, NonValue } from '@/shared/analytics';
import { whatWorksQuery } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

const partOf = (parts: readonly { dimension: string; value: string | null }[], dimension: string) =>
  parts.find((part) => part.dimension === dimension)?.value ?? null;

/** R21: instruments ranked by the credible lower bound of expectancy; those below the minimum follow with their n, never hidden. */
// A universe above the endpoint's cap (2,000 cells) is refused 422 by the server; the panel prints that message (QueryPanel) instead of a cut list, by design.
export function InstrumentScorecard({ query }: ReportPanelProps) {
  const result = useQuery(whatWorksQuery(query, 'instrument'));
  const byStrategy = useQuery(whatWorksQuery(query, 'instrument,strategy'));
  // The best strategy of an instrument is its best ranked cell (cells arrive in rank order).
  const best = new Map<string, string>();
  for (const cell of byStrategy.data?.ranked ?? []) {
    const symbol = partOf(cell.group.parts, 'instrument');
    const strategy = partOf(cell.group.parts, 'strategy');
    if (symbol && strategy && !best.has(symbol)) best.set(symbol, strategy);
  }
  const bestText = (symbol: string | null) =>
    byStrategy.isError
      ? 'not available'
      : byStrategy.isPending
        ? 'loading…'
        : ((symbol && best.get(symbol)) ?? 'none ranked');
  return (
    <QueryPanel title="Instrument scorecard" query={result}>
      {(w) => {
        const min = w.minTrades;
        return (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            <Typography variant="body1" sx={{ fontWeight: 600 }}>
              {w.headline}
            </Typography>
            {w.ranked.length === 0 ? (
              <NonValue state={{ kind: 'empty', reason: 'No instrument is ranked.' }} />
            ) : (
              <DataTable
                label="Instruments ranked"
                rows={w.ranked}
                rowKey={(row) => row.group.key}
                columns={[
                  { kind: 'text', id: 'rank', header: 'Rank', render: (row) => formatCount(row.rank) },
                  {
                    kind: 'text',
                    id: 'symbol',
                    header: 'Instrument',
                    render: (row) => partOf(row.group.parts, 'instrument') ?? 'Not recorded',
                  },
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
                    id: 'total',
                    header: 'Total R',
                    metric: (row) => metricFromApi(row.group.totalR, row.group.n, min),
                    format: formatR,
                    tone: 'gain-loss',
                  },
                  {
                    kind: 'text',
                    id: 'best',
                    header: 'Best strategy',
                    render: (row) => bestText(partOf(row.group.parts, 'instrument')),
                  },
                ]}
              />
            )}
            <Typography variant="h3" component="h3" sx={{ fontSize: 'body1.fontSize' }}>
              Not ranked
            </Typography>
            <DataTable
              label="Instruments not ranked"
              rows={w.notRanked}
              rowKey={(row) => row.group.key}
              columns={[
                {
                  kind: 'text',
                  id: 'symbol',
                  header: 'Instrument',
                  render: (row) => partOf(row.group.parts, 'instrument') ?? 'Not recorded',
                },
                { kind: 'text', id: 'n', header: 'n', align: 'right', render: (row) => formatCount(row.group.n) },
                { kind: 'text', id: 'reason', header: 'Why not ranked', render: (row) => row.reason },
              ]}
            />
            <Typography variant="body2" color="text.secondary">
              {`${w.selectionNote} Looked at ${formatCount(w.cellsLookedAt)} instruments; about ${w.expectedChancePositives.toFixed(1)} positive lower bounds are expected by chance. No equity sparkline per instrument.`}
            </Typography>
            <EvidenceBlock {...evidenceOf(w.evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

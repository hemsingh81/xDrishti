import { Box, Button, Chip, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import {
  DataTable,
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  formatRatio,
  formatSigned,
  type MetricState,
} from '@/shared/analytics';
import {
  benchmarksQuery,
  groupsQuery,
  whatWorksQuery,
  type BenchmarksView,
  type GroupView,
  type WhatWorksView,
} from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import { byCredibleBound } from '../lib/ranking';
import type { ReportQuery } from '../lib/reportFilter';
import type { CellInfo } from './CellPanel';
import { QueryPanel } from './Panel';

const basketOf = (group: GroupView) => group.parts.find((p) => p.dimension === 'basket')?.value ?? null;
const percent = (value: number) => `${formatSigned(value)} %`;

/** The server's best strategy for a basket: its top ranked cell, or why none is ranked. */
function bestStrategy(w: WhatWorksView | undefined, basket: string | null): string {
  if (!w) return 'Not available';
  const cell = w.ranked.find((r) => basketOf(r.group) === basket);
  if (cell)
    return `${cell.group.parts.find((p) => p.dimension === 'strategy')?.value ?? 'Not recorded'} (rank ${formatCount(cell.rank)})`;
  const why = w.notRanked.find((r) => basketOf(r.group) === basket)?.reason;
  return `None ranked${why ? `: ${why}` : ''}`;
}

function Excluded({ bench, name }: { bench: BenchmarksView; name: (id: string) => string }) {
  const list = bench.baskets.flatMap((b) => b.excluded.map((e) => ({ basket: b.basketId, ...e })));
  if (list.length === 0) return null;
  const reasons = [...new Set(list.map((e) => e.reason))].join(', ');
  return (
    <details>
      <summary>{`${formatCount(list.length)} instruments left out (${reasons})`}</summary>
      <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
        {list.map((e) => (
          <li key={`${e.basket}${e.instrumentId}`}>
            <Typography variant="body2">{`${name(e.basket)}: ${e.symbol}, ${e.reason}`}</Typography>
          </li>
        ))}
      </Box>
    </details>
  );
}

interface ComparisonTableProps {
  query: ReportQuery;
  onOpen: (info: CellInfo) => void;
}

/**
 * Baskets side by side. Figures are the server's, ordered by the credible lower bound. "Best" is the What works rank, not
 * a page rule. The buy-and-hold and index columns are `unavailable` when the benchmarks call fails; the rest stays.
 */
export function ComparisonTable({ query, onOpen }: ComparisonTableProps) {
  const groups = useQuery(groupsQuery(query, 'basket'));
  const works = useQuery(whatWorksQuery(query, 'basket,strategy'));
  const bench = useQuery(benchmarksQuery(query));
  const names = useBasketNames();
  const min = works.data?.minTrades ?? null;
  const top = works.data?.ranked[0];
  const topStrategy = top?.group.parts.find((p) => p.dimension === 'strategy')?.value ?? 'Not recorded';
  const bestBasket = top?.lowerBoundPositive ? basketOf(top.group) : undefined;
  const hold = (id: string | null): MetricState => {
    const entry = bench.data?.baskets.find((b) => b.basketId === id);
    return entry
      ? metricFromApi(entry.buyAndHold, entry.included, bench.data?.minMembers)
      : { kind: 'unavailable', reason: 'Benchmarks are not available.' };
  };
  const index: MetricState = bench.data
    ? metricFromApi(bench.data.index.return)
    : { kind: 'unavailable', reason: 'Benchmarks are not available.' };
  return (
    <QueryPanel title="Basket comparison" query={groups}>
      {({ groups: list, nonAdditive, evidence }) => {
        const rows = [...list].sort(byCredibleBound);
        const open = (g: GroupView) => {
          onOpen({
            key: g.key,
            title: names.part('basket', basketOf(g)),
            n: g.n,
            state: metricFromApi(g.expectancyR, g.n, min),
            format: formatR,
            evidence,
            group: g,
            min,
            parts: g.parts,
          });
        };
        return (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            <DataTable
              label="Basket comparison"
              rows={rows}
              rowKey={(g) => g.key}
              columns={[
                {
                  kind: 'text',
                  id: 'basket',
                  header: 'Basket',
                  render: (g) => (
                    <>
                      <Button
                        size="small"
                        onClick={() => {
                          open(g);
                        }}
                        sx={{ minHeight: 44, textAlign: 'left' }}
                      >
                        {names.part('basket', basketOf(g))}
                      </Button>
                      {bestBasket !== undefined && basketOf(g) === bestBasket && (
                        <Chip size="small" label={`Top ranked cell: ${topStrategy}`} sx={{ ml: 0.5 }} />
                      )}
                    </>
                  ),
                },
                { kind: 'text', id: 'trades', header: 'Trades', align: 'right', render: (g) => formatCount(g.n) },
                {
                  kind: 'metric',
                  id: 'win',
                  header: 'Win rate',
                  metric: (g) => metricFromApi(g.winRate, g.n, min),
                  n: (g) => g.n,
                  format: formatRate,
                },
                {
                  kind: 'metric',
                  id: 'exp',
                  header: 'Expectancy',
                  metric: (g) => metricFromApi(g.expectancyR, g.n, min),
                  n: (g) => g.n,
                  format: formatR,
                  tone: 'gain-loss',
                },
                {
                  kind: 'metric',
                  id: 'tot',
                  header: 'Total R',
                  metric: (g) => metricFromApi(g.totalR, g.n, min),
                  n: (g) => g.n,
                  format: formatR,
                  tone: 'gain-loss',
                },
                {
                  kind: 'metric',
                  id: 'pf',
                  header: 'Profit factor',
                  metric: (g) => metricFromApi(g.profitFactor, g.n, min),
                  n: (g) => g.n,
                  format: formatRatio,
                },
                {
                  kind: 'metric',
                  id: 'dd',
                  header: 'Max drawdown',
                  metric: (g) => metricFromApi(g.maxDrawdownR, g.n, min),
                  n: (g) => g.n,
                  format: formatR,
                },
                {
                  kind: 'text',
                  id: 'best',
                  header: 'Best strategy',
                  render: (g) => bestStrategy(works.data, basketOf(g)),
                },
                {
                  kind: 'metric',
                  id: 'hold',
                  header: 'Buy and hold',
                  metric: (g) => hold(basketOf(g)),
                  format: percent,
                },
                { kind: 'metric', id: 'index', header: 'Index', metric: () => index, format: percent },
              ]}
            />
            {bench.data && <Excluded bench={bench.data} name={(id) => names.name(id)} />}
            {nonAdditive && (
              <Typography variant="body2" color="text.secondary">
                {NON_ADDITIVE_NOTE}
              </Typography>
            )}
            <Typography variant="body2" color="text.secondary">
              No rupee column: replayed trades are unsized and not costed on confirmed rates. Buy and hold is a price
              return, equal weight, dividends not included.
            </Typography>
            <EvidenceBlock {...evidenceOf(evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}

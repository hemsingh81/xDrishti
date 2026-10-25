import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import {
  DataTable,
  EvidenceBlock,
  formatCount,
  formatR,
  formatRatio,
  NonValue,
  type MetricState,
} from '@/shared/analytics';
import { comparisonsQuery, groupsQuery, type GroupView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { sameRunSet } from '../lib/runSet';
import { ComparisonPicker } from './ComparisonPicker';
import { ExitSelectionsPanel } from './ExitSelectionsPanel';
import { QueryPanel } from './Panel';

const exitOf = (g: GroupView) => g.parts.find((p) => p.dimension === 'exit')?.value ?? null;

/** R5: the exits side by side on identical entries (the report engine over the member runs, split by exit), and the stored out-of-sample choices. */
export function ExitComparison({ query, min }: ReportPanelProps) {
  const chosen = query.runs !== undefined;
  const stored = useQuery(comparisonsQuery);
  const chosenKeys = query.runs?.split(',') ?? [];
  // Like for like only when the chosen runs are exactly the members of a stored comparison (the same entries by construction).
  const isStored =
    stored.data?.comparisons.some((c) =>
      sameRunSet(
        c.members.map((m) => m.runKey),
        chosenKeys,
      ),
    ) ?? false;
  const result = useQuery({ ...groupsQuery(query, 'exit'), enabled: chosen });
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
    metric: (g: GroupView): MetricState => metricFromApi(pick(g), g.n, min),
    format,
    tone: gain ? ('gain-loss' as const) : undefined,
  });
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <ComparisonPicker />
      {chosen ? (
        <QueryPanel title="Exits on identical entries" query={result}>
          {({ groups, evidence }) => {
            const sameN = groups.length >= 2 && groups.every((g) => g.n === groups[0]?.n);
            return (
              <Box sx={{ display: 'grid', gap: 1.5 }}>
                <DataTable
                  label="Exits on identical entries"
                  rows={groups}
                  rowKey={(g) => g.key}
                  columns={[
                    { kind: 'text', id: 'exit', header: 'Exit', render: (g) => exitOf(g) ?? 'Not recorded' },
                    { kind: 'text', id: 'n', header: 'n', align: 'right', render: (g) => formatCount(g.n) },
                    figure('exp', 'Expectancy', (g) => g.expectancyR, formatR, true),
                    figure('total', 'Total R', (g) => g.totalR, formatR, true),
                    figure('pf', 'Profit factor', (g) => g.profitFactor, formatRatio),
                    figure('dd', 'Max drawdown', (g) => g.maxDrawdownR, formatR),
                    figure('streak', 'Longest loss streak', (g) => g.longestLossStreak, formatCount),
                    figure('lower', 'Expectancy lower bound', (g) => g.expectancyLowerR, formatR, true),
                  ]}
                />
                <Typography variant="body2" color="text.secondary">
                  {!sameN
                    ? 'n differs between the rows, or there is only one row, so these runs do not share every entry (a filter or a different run set): do not read the rows as a like-for-like comparison.'
                    : isStored
                      ? 'Same entries, different exits: n is the same in every row.'
                      : 'n is the same in every row, but these runs are not the members of one stored comparison, so the entries may still differ.'}{' '}
                  In-sample, modelled fills; the lower bound is not a ranking by itself and no exit is validated here.
                </Typography>
                <EvidenceBlock {...evidenceOf(evidence)} compact />
              </Box>
            );
          }}
        </QueryPanel>
      ) : (
        <NonValue state={{ kind: 'empty', reason: 'Choose a stored comparison to put its exits side by side.' }} />
      )}
      <ExitSelectionsPanel />
    </Box>
  );
}

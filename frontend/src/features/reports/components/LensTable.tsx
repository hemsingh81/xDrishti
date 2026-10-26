import { Alert, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ExportMenu } from '@/features/exports';
import { DataTable, formatCount, formatR, formatRate } from '@/shared/analytics';
import {
  dimensionReasonQuery,
  groupsQuery,
  summaryQuery,
  type EvidenceView,
  type GroupView,
} from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { dimensionLabel } from '../lib/pivotLimits';
import { lensToQuery, type Lens } from '../lib/lensToQuery';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportQuery } from '../lib/reportFilter';
import { Button } from '@mui/material';
import type { CellInfo } from './CellPanel';
import { QueryPanel } from './Panel';

interface TableProps {
  title: string;
  groups: readonly GroupView[];
  evidence: EvidenceView;
  min: number | null;
  dimension: string;
  nonAdditive?: boolean;
  onOpen: (info: CellInfo) => void;
}

function GroupTable({ title, groups, evidence, min, dimension, nonAdditive, onOpen }: TableProps) {
  const names = useBasketNames();
  const nameOf = (g: GroupView) =>
    g.parts.length === 0 ? 'All trades' : names.part(dimension, g.parts[0]?.value ?? null);
  return (
    <Box>
      <DataTable
        label={title}
        rows={groups}
        rowKey={(g) => g.key}
        evidence={evidenceOf(evidence)}
        columns={[
          {
            kind: 'text',
            id: 'group',
            header: dimension === 'all' ? 'Group' : dimensionLabel(dimension),
            render: (g) => (
              <Button
                size="small"
                sx={{ minHeight: 44, textAlign: 'left' }}
                onClick={() => {
                  onOpen({
                    key: g.key,
                    title: nameOf(g),
                    n: g.n,
                    state: metricFromApi(g.expectancyR, g.n, min),
                    format: formatR,
                    evidence,
                    group: g,
                    min,
                    parts: g.parts,
                  });
                }}
              >
                {nameOf(g)}
              </Button>
            ),
          },
          { kind: 'text', id: 'n', header: 'Trades', align: 'right', render: (g) => formatCount(g.n) },
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
        ]}
      />
      {nonAdditive && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          {NON_ADDITIVE_NOTE}
        </Typography>
      )}
    </Box>
  );
}

function Summary({ query, min, onOpen }: Omit<LensTableProps, 'lens'>) {
  const result = useQuery(summaryQuery(query));
  return (
    <QueryPanel title="Groups by lens" query={result}>
      {(s) => (
        <GroupTable
          title="All trades"
          groups={[s.all]}
          evidence={s.evidence}
          min={min}
          dimension="all"
          onOpen={onOpen}
        />
      )}
    </QueryPanel>
  );
}

function ByDimension({ query, min, onOpen, by }: Omit<LensTableProps, 'lens'> & { by: string }) {
  const result = useQuery(groupsQuery(query, by));
  return (
    <QueryPanel title="Groups by lens" query={result}>
      {(g) => (
        <>
          <ExportMenu kind="report-groups" query={{ ...query, lens: by }} />
          <GroupTable
            title={`Groups by ${by}`}
            groups={g.groups}
            evidence={g.evidence}
            min={min}
            dimension={by}
            nonAdditive={g.nonAdditive}
            onOpen={onOpen}
          />
        </>
      )}
    </QueryPanel>
  );
}

function Refused() {
  const reason = useQuery(dimensionReasonQuery('account'));
  return <Alert severity="info">{reason.data ?? 'By account is not available.'}</Alert>;
}

interface LensTableProps {
  lens: Lens;
  query: ReportQuery;
  min: number | null;
  onOpen: (info: CellInfo) => void;
}

/** The groups table of the chosen lens: the summary for all trades, else the groups `by` that dimension. */
export function LensTable({ lens, ...rest }: LensTableProps) {
  const target = lensToQuery(lens);
  return (
    <Box>
      {target.kind === 'summary' && <Summary {...rest} />}
      {target.kind === 'groups' && <ByDimension {...rest} by={target.by} />}
      {target.kind === 'later' && <Alert severity="info">{target.reason}</Alert>}
      {target.kind === 'refused' && <Refused />}
    </Box>
  );
}

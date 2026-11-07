import { Link, TextField } from '@mui/material';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { DataTable, formatCount, metricOf } from '@/shared/analytics';
import type { ScorecardSummary } from '../api/readinessQueries';
import { checklistSize, formatScore } from '../lib/words';
import { Panel } from './Panel';
import { LabelChip } from './StatusChips';

const sorts = {
  weakest: { label: 'Weakest first', compare: (a: ScorecardSummary, b: ScorecardSummary) => a.score - b.score },
  strongest: { label: 'Strongest first', compare: (a: ScorecardSummary, b: ScorecardSummary) => b.score - a.score },
  rows: {
    label: 'Most rows passed',
    compare: (a: ScorecardSummary, b: ScorecardSummary) => b.rowsPassed - a.rowsPassed,
  },
  name: { label: 'Name', compare: (a: ScorecardSummary, b: ScorecardSummary) => a.strategy.localeCompare(b.strategy) },
} as const;
type SortId = keyof typeof sorts;
const sortIds = Object.keys(sorts) as SortId[];

const open = (row: ScorecardSummary): string =>
  `?strategy=${encodeURIComponent(row.strategy)}${row.version === null ? '' : `&version=${String(row.version)}`}`;

/** One line per strategy at its latest Lab version, sortable. The strategy name opens its scorecard below. */
export function StrategyTable({ strategies }: { strategies: readonly ScorecardSummary[] }) {
  const [sort, setSort] = useState<SortId>('weakest');
  const rows = [...strategies].sort(sorts[sort].compare);
  return (
    <Panel
      title="Strategies"
      actions={
        <TextField
          select
          size="small"
          label="Sort by"
          value={sort}
          onChange={(event) => {
            setSort(event.target.value as SortId);
          }}
          slotProps={{ select: { native: true } }}
          sx={{ minWidth: 180 }}
        >
          {sortIds.map((id) => (
            <option key={id} value={id}>
              {sorts[id].label}
            </option>
          ))}
        </TextField>
      }
    >
      <DataTable
        label="Strategy readiness"
        rows={rows}
        rowKey={(row) => `${row.strategy}:${String(row.version)}`}
        status={
          strategies.length === 0
            ? { kind: 'empty', reason: 'No strategy in the Lab yet. Run and register a strategy first.' }
            : undefined
        }
        columns={[
          {
            kind: 'text',
            id: 'strategy',
            header: 'Strategy',
            render: (row) => (
              <Link component={RouterLink} to={open(row)}>
                {row.strategy}
                {row.version === null ? ' (no Lab version)' : ` v${String(row.version)}`}
              </Link>
            ),
          },
          {
            kind: 'text',
            id: 'label',
            header: 'Label',
            render: (row) => <LabelChip code={row.label} text={row.labelText} />,
          },
          {
            kind: 'metric',
            id: 'score',
            header: 'Score (of 100)',
            metric: (row) => metricOf(row.score, 'The score is not a number.'),
            format: formatScore,
          },
          { kind: 'text', id: 'weakest', header: 'Weakest link', render: (row) => row.weakestSubScore ?? 'None' },
          {
            kind: 'text',
            id: 'rows',
            header: `Rows passed of ${String(checklistSize)}`,
            render: (row) => `${formatCount(row.rowsPassed)} of ${String(checklistSize)}`,
            align: 'right',
          },
          {
            kind: 'text',
            id: 'hard',
            header: 'Hard fails',
            render: (row) =>
              row.hardFails.length === 0 ? 'None' : `${String(row.hardFails.length)}: ${row.hardFails.join('; ')}`,
          },
          { kind: 'text', id: 'gateR', header: 'Gate R', render: (row) => (row.gateR ? 'Passes' : 'Does not pass') },
          { kind: 'text', id: 'gateS', header: 'Gate S', render: (row) => (row.gateS ? 'Passes' : 'Does not pass') },
        ]}
      />
    </Panel>
  );
}

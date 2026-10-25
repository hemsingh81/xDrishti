import { Checkbox } from '@mui/material';
import { DataTable, formatCount, formatRange } from '@/shared/analytics';
import type { RunItem } from '../api/reportQueries';

interface RunPickerProps {
  runs: readonly RunItem[];
  /** The run keys named in the address; empty = the server's default run set. */
  chosen: readonly string[];
  /** The runs behind the numbers on screen; `null` while the report has not answered. */
  used: ReadonlySet<string> | null;
  /** The new set of run keys, or `null` for the default run set. */
  onChange: (keys: string[] | null) => void;
}

const statusOf = (run: RunItem, used: ReadonlySet<string> | null, chosen: boolean): string => {
  if (used?.has(run.runKey)) return 'Used';
  if (used)
    return chosen || run.inDefaultSet ? 'Left out by the report' : `Left out: ${run.defaultSetNote ?? 'not chosen'}`;
  return run.inDefaultSet
    ? 'In the default run set'
    : `Not in the default run set: ${run.defaultSetNote ?? 'no reason given'}`;
};

/** Every stored run with its facts and whether the numbers use it; the checkboxes set `runs=` in the address. */
export function RunPicker({ runs, chosen, used, onChange }: RunPickerProps) {
  const defaults = runs.filter((run) => run.inDefaultSet).map((run) => run.runKey);
  const checked = chosen.length > 0 ? chosen : defaults;
  const toggle = (key: string) => {
    const next = checked.includes(key) ? checked.filter((k) => k !== key) : [...checked, key];
    const sameAsDefault = next.length === defaults.length && defaults.every((k) => next.includes(k));
    onChange(next.length === 0 || sameAsDefault ? null : next);
  };
  return (
    <DataTable
      label="Runs behind the numbers"
      rows={runs}
      rowKey={(run) => run.runKey}
      columns={[
        {
          kind: 'text',
          id: 'use',
          header: 'Use',
          render: (run) => (
            <Checkbox
              checked={checked.includes(run.runKey)}
              onChange={() => {
                toggle(run.runKey);
              }}
              slotProps={{
                input: { 'aria-label': `Use run ${run.strategyId} ${run.universeLabel} ${run.windowStart}` },
              }}
              sx={{ p: 1.25 }}
            />
          ),
        },
        {
          kind: 'text',
          id: 'run',
          header: 'Strategy and universe',
          render: (run) => `${run.strategyId}, ${run.universeLabel}`,
        },
        {
          kind: 'text',
          id: 'window',
          header: 'Window',
          render: (run) => formatRange({ from: run.windowStart, to: run.windowEnd }),
        },
        { kind: 'text', id: 'basis', header: 'Costs', render: (run) => (run.costed ? 'Costed (net R)' : 'Gross R') },
        { kind: 'text', id: 'style', header: 'Style', render: (run) => run.style ?? 'Not recorded' },
        { kind: 'text', id: 'exit', header: 'Exit', render: (run) => run.exit ?? 'Not recorded' },
        {
          kind: 'text',
          id: 'source',
          header: 'Label source',
          render: (run) => `style ${run.styleSource ?? 'none'}, exit ${run.exitSource ?? 'none'}`,
        },
        {
          kind: 'text',
          id: 'trades',
          header: 'Closed trades',
          align: 'right',
          render: (run) => formatCount(run.closedTrades),
        },
        {
          kind: 'text',
          id: 'status',
          header: 'Status',
          render: (run) => statusOf(run, used, chosen.includes(run.runKey)),
        },
      ]}
    />
  );
}

import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import PauseIcon from '@mui/icons-material/PauseCircleOutlined';
import { Chip } from '@mui/material';
import { DataTable, formatCount } from '@/shared/analytics';
import type { DecayKill } from '../api/confidenceQueries';
import { killStateLabel, ruleLabel } from '../lib/confidenceText';
import { figure, formatFine, formatFineSigned } from '../lib/figure';
import { Panel } from './Panel';

const looks = {
  fires: { color: 'error', Icon: CancelIcon },
  // Holds is a plain result, not praise: it keeps the neutral colour.
  holds: { color: 'default', Icon: CheckIcon },
  // Held and not evaluated are NOT a pass: a help icon, the words, no success colour.
  held: { color: 'warning', Icon: PauseIcon },
  not_evaluated: { color: 'default', Icon: HelpIcon },
} as const;

/** A kill-rule state as an icon and words; colour is an addition. `held` and `not_evaluated` say "not a pass". */
export function KillStateChip({ state }: { state: string }) {
  const look = state in looks ? looks[state as keyof typeof looks] : looks.not_evaluated;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look.color}
      icon={<look.Icon aria-hidden />}
      label={killStateLabel(state)}
    />
  );
}

/** The four kill rules on the edge series, with their numbers. An evaluation only: nothing here pauses anything. */
export function KillRulesPanel({ rules }: { rules: readonly DecayKill[] }) {
  return (
    <Panel title="Kill rules">
      <DataTable
        label="Kill rules and their numbers"
        rows={rules}
        rowKey={(row) => row.rule}
        status={
          rules.length === 0 ? { kind: 'empty', reason: 'The server returned no kill-rule evaluation.' } : undefined
        }
        columns={[
          {
            kind: 'text',
            id: 'rule',
            header: 'Rule',
            render: (row) => (row.proposalOnly ? `${ruleLabel(row.rule)} (proposal only)` : ruleLabel(row.rule)),
          },
          { kind: 'text', id: 'state', header: 'State', render: (row) => <KillStateChip state={row.state} /> },
          {
            kind: 'metric',
            id: 'value',
            header: 'Figure judged',
            metric: (row) => figure(row.value, 'not evaluated'),
            format: formatFineSigned,
          },
          {
            kind: 'metric',
            id: 'limit',
            header: 'Limit',
            metric: (row) => figure(row.limit, 'not stated'),
            format: formatFine,
          },
          { kind: 'text', id: 'trades', header: 'Trades', render: (row) => formatCount(row.trades), align: 'right' },
          { kind: 'text', id: 'text', header: 'In words', render: (row) => row.text },
        ]}
      />
    </Panel>
  );
}

import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { VerdictChip, type Verdict } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { LoadingState } from '@/shared/ui';
import { phaseCheckQuery } from '../api/viewsApi';
import { RetryError } from './RetryError';
import { StackedTable, type Col } from './StackedTable';

type Account = Schemas['AccountPhaseReport'];
type Session = Schemas['SessionLine'];

const verdicts: Record<string, Verdict> = { MET: 'met', 'NOT MET': 'not_met', 'NOT EVALUABLE': 'not_evaluable' };
// Anything the server says that is not exactly MET or NOT MET reads as not evaluable: never green by accident.
const verdictOf = (word: string): Verdict => verdicts[word] ?? 'not_evaluable';

const classes: Record<string, { word: string; Icon: typeof CheckIcon }> = {
  counted: { word: 'Counted', Icon: CheckIcon },
  idle: { word: 'Idle', Icon: RemoveIcon },
  broken: { word: 'Broken', Icon: WarningIcon },
  out_of_mode: { word: 'Out of mode', Icon: BlockIcon },
};

function ClassCell({ value }: { value: string }) {
  const { word, Icon } = classes[value] ?? { word: value, Icon: HelpIcon };
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
      <Icon fontSize="small" aria-hidden />
      {word}
    </Box>
  );
}

const columns: readonly Col<Session>[] = [
  { id: 'date', header: 'Date', render: (s) => s.date },
  { id: 'class', header: 'Class', render: (s) => <ClassCell value={s.class} /> },
  { id: 'orders', header: 'Orders sent', align: 'right', render: (s) => s.ordersSent },
  {
    id: 'breaks',
    header: 'Breaks',
    render: (s) => (s.breaks > 0 ? `${s.breaks} (${s.breakKinds.join(', ')})` : 'none'),
  },
  { id: 'why', header: 'Why', render: (s) => (s.reasons.length > 0 ? s.reasons.join(', ') : 'none') },
];

const list = (label: string, items: readonly string[]) => (
  <Box component="ul" aria-label={label} sx={{ m: 0, pl: 3 }}>
    {items.map((t) => (
      <li key={t}>
        <Typography variant="body2">{t}</Typography>
      </li>
    ))}
  </Box>
);

function AccountCard({ a, needed }: { a: Account; needed: number }) {
  const heading = `${a.name} phase check`;
  return (
    <Box component="section" aria-label={heading} sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 2 }}>
      <Stack sx={{ gap: 1.5 }}>
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="subtitle1" component="h3">
            {a.name}
          </Typography>
          <VerdictChip verdict={verdictOf(a.verdict)} />
        </Stack>
        <Typography>
          {a.windowStart
            ? `${a.counted} of ${needed} counted sessions since ${a.windowStart}`
            : `${a.counted} of ${needed} counted sessions (no semi-auto window yet)`}
        </Typography>
        <Typography variant="body2">{a.label}</Typography>
        <Typography variant="body2">
          {a.lastBreak ? `Last break: ${a.lastBreak}` : 'No break in the window.'}
        </Typography>
        {a.facts.length > 0 &&
          list(
            `${a.name} facts`,
            a.facts.map((f) => `${f.name}: ${f.state}. ${f.text}`),
          )}
        {a.sessions.length === 0 ? (
          <Typography variant="body2">No sessions recorded.</Typography>
        ) : (
          <StackedTable label={`${a.name} sessions`} columns={columns} rows={a.sessions} rowKey={(s) => s.date} />
        )}
      </Stack>
    </Box>
  );
}

/** Modes tab: 30 consecutive proved semi-auto sessions per account (P12-12). Read only: it has no button that writes. */
export function PhaseCheckPanel() {
  const { data, error, isPending, refetch } = useQuery(phaseCheckQuery);
  return (
    <Box component="section" aria-label="Phase check" sx={{ display: 'grid', gap: 2 }}>
      <Typography variant="h2" sx={{ fontSize: '1.25rem' }}>
        Phase check: semi-auto sessions
      </Typography>
      {isPending && <LoadingState label="Loading the phase check" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data && (
        <>
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography component="span">Phase exit:</Typography>
            <VerdictChip verdict={verdictOf(data.report.exitVerdict)} />
            <Typography component="span">{data.report.exitCriterion}</Typography>
          </Stack>
          <Typography variant="body2">Judged through {data.report.through}.</Typography>
          {data.report.accounts.length === 0 && <Typography role="status">No Trading account to judge.</Typography>}
          {data.report.accounts.map((a) => (
            <AccountCard key={a.accountId} a={a} needed={data.report.needed} />
          ))}
          <Typography variant="body2">
            {data.report.boundary.name}: {data.report.boundary.text}
          </Typography>
          {list('Phase check limits', data.report.limits)}
        </>
      )}
    </Box>
  );
}

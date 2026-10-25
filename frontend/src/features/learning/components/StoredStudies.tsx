import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatDate } from '@/shared/lib/format';
import {
  formatCount,
  formatR,
  formatRange,
  formatRate,
  formatSigned,
  MetricValue,
  metricOf,
  NonValue,
} from '@/shared/analytics';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import {
  entryWindowsQuery,
  metaPolicyQuery,
  type EntryWindowStudy,
  type MetaPolicyStudy,
  type SmartRule,
} from '../api/learningQueries';

const statusText: Record<string, string> = {
  Proposed: 'Proposed',
  Inconclusive: 'Inconclusive (a hypothesis only)',
  NoStableWindow: 'No stable window',
  NotEnoughEvidence: 'Not enough evidence',
};

const outcomeText: Record<string, string> = {
  NoPolicy: 'No policy',
  NotEnoughEvidence: 'Not enough evidence',
  DoesNotHelp: 'Does not help',
  Inconclusive: 'Inconclusive',
  Helps: 'Helps',
};

const stamp = (by: string | null | undefined, at: string | null | undefined, id: number | null | undefined) =>
  `Stored study #${formatCount(id)} by ${by ?? 'unknown'}${at ? ` on ${formatDate(at.slice(0, 10))}` : ''}.`;

function Missing({ rule, command }: { rule: SmartRule; command: string }) {
  return (
    <>
      <NonValue state={{ kind: 'not_evaluable', reason: rule.outcome.reason }} />
      <Typography variant="caption" color="text.secondary">
        None stored yet. Run <code>{command}</code> (it does nothing on its own; the owner runs it).
      </Typography>
    </>
  );
}

function Limits({ items }: { items: readonly string[] }) {
  return (
    <Box component="ul" aria-label="Study limits" sx={{ m: 0, pl: 2.5 }}>
      {items.map((limit) => (
        <li key={limit}>
          <Typography variant="caption">{limit}</Typography>
        </li>
      ))}
    </Box>
  );
}

function WindowsStudy({ study }: { study: EntryWindowStudy }) {
  return (
    <Box sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2">
        {stamp(study.builtBy, study.createdAt, study.id)} {formatCount(study.rows)} trades,{' '}
        {study.from && study.to ? formatRange({ from: study.from, to: study.to }) : 'no range'},{' '}
        {formatCount(study.usedFolds)} folds, buckets of {study.knobs.bucketMinutes} min, hold-out{' '}
        {study.knobs.holdOutMonths} months. Basis: {study.basis}.
      </Typography>
      <Box component="ul" aria-label="Entry windows by cell" sx={{ m: 0, pl: 2.5 }}>
        {study.cells.map((c) => (
          <li key={c.cellKey}>
            <Typography variant="body2" component="span">
              <strong>{c.cellKey}</strong>: {statusText[c.status] ?? c.status}
              {c.windowFrom && c.windowTo ? `, window ${c.windowFrom} to ${c.windowTo}` : ', no window'} (cell n{' '}
              {formatCount(c.cellTrades)}
              {c.windowFrom ? `, window n ${formatCount(c.windowTrades)}, ${formatRate(c.keptShare)} kept` : ''}).
            </Typography>
            {c.windowFrom && (
              <Typography variant="body2" component="span">
                {' '}
                Win rate <MetricValue state={metricOf(c.shrunkRate)} format={formatRate} /> against the cell{' '}
                <MetricValue state={metricOf(c.cellWinRate)} format={formatRate} />, lift{' '}
                <MetricValue state={metricOf(c.lift)} format={formatSigned} />, mean R inside{' '}
                <MetricValue state={metricOf(c.meanRInside)} format={formatR} /> and outside{' '}
                <MetricValue state={metricOf(c.meanROutside)} format={formatR} />. Held in {formatCount(c.foldsHeld)} of{' '}
                {formatCount(c.foldsEvaluable)} evaluable folds ({formatCount(c.foldsRequired)} required); pooled
                out-of-sample lift <MetricValue state={metricOf(c.pooledLift)} format={formatSigned} />.
                {c.removesMajority && ' Flagged for review: the window drops more than half of the cell.'}
              </Typography>
            )}
            <Box component="ul" aria-label={`${c.cellKey} reasons`} sx={{ m: 0, pl: 2 }}>
              {c.reasons.map((reason) => (
                <li key={reason}>
                  <Typography variant="caption">{reason}</Typography>
                </li>
              ))}
            </Box>
          </li>
        ))}
      </Box>
      <Limits items={study.limits} />
    </Box>
  );
}

function MetaStudy({ study }: { study: MetaPolicyStudy }) {
  const p = study.pooled;
  return (
    <Box sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2">
        {stamp(study.builtBy, study.createdAt, study.id)} Outcome:{' '}
        <strong>{outcomeText[study.outcome] ?? study.outcome}</strong>. Basis: {study.basis}.
      </Typography>
      <Box component="ul" aria-label="Policy reasons" sx={{ m: 0, pl: 2.5 }}>
        {study.reasons.map((reason) => (
          <li key={reason}>
            <Typography variant="body2">{reason}</Typography>
          </li>
        ))}
      </Box>
      {study.folds.length > 0 && (
        <Typography variant="body2">
          Out of sample over {formatCount(study.usedFolds)} usable folds: kept {formatCount(p.kept)}, mean R{' '}
          <MetricValue state={metricOf(p.meanKept)} format={formatR} />; skipped {formatCount(p.skipped)}, mean R{' '}
          <MetricValue state={metricOf(p.meanSkipped)} format={formatR} />. Lift against taking everything{' '}
          <MetricValue state={metricOf(p.lift)} format={formatSigned} /> R per candidate, against{' '}
          <MetricValue state={metricOf(p.randomLift)} format={formatSigned} /> for a random skip. Last{' '}
          {formatCount(study.recent.candidates)}: kept mean R{' '}
          <MetricValue state={metricOf(study.recent.meanKept)} format={formatR} />, skipped{' '}
          <MetricValue state={metricOf(study.recent.meanSkipped)} format={formatR} />.
        </Typography>
      )}
      {study.policy.length > 0 && (
        <Box component="ul" aria-label="Proposed thresholds (chosen in-sample)" sx={{ m: 0, pl: 2.5 }}>
          {study.policy.map((t) => (
            <li key={t.group}>
              <Typography variant="body2">
                Group {t.group}: {t.threshold === null ? 'take all' : `take at ${t.threshold.toFixed(2)} or more`} (n{' '}
                {formatCount(t.rows)}, keeps {formatRate(t.keptShare)})
              </Typography>
            </li>
          ))}
        </Box>
      )}
      <Limits items={study.limits} />
    </Box>
  );
}

const windowsCommand = 'scripts/xd.sh learn windows --run RUNKEY:STYLE:EXIT … --store';
const policyCommand = 'scripts/xd.sh learn policy --run RUNKEY:STYLE:EXIT … --store';

function WindowsStored({ rule }: { rule: SmartRule }) {
  const query = useQuery(entryWindowsQuery);
  if (query.isPending) return <LoadingState label="Loading the stored study…" />;
  if (query.error instanceof ApiError && query.error.status === 404)
    return <Missing rule={rule} command={windowsCommand} />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <WindowsStudy study={query.data} />;
}

function MetaStored({ rule }: { rule: SmartRule }) {
  const query = useQuery(metaPolicyQuery);
  if (query.isPending) return <LoadingState label="Loading the stored study…" />;
  if (query.error instanceof ApiError && query.error.status === 404)
    return <Missing rule={rule} command={policyCommand} />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <MetaStudy study={query.data} />;
}

/** The stored result of the rule's own study, or the named non-value with the command that makes one. Read only: nothing is computed here. */
export function StoredStudy({ rule }: { rule: SmartRule }) {
  return rule.id === 'windows' ? <WindowsStored rule={rule} /> : <MetaStored rule={rule} />;
}

import { Box, Chip, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { EvidenceBlock, formatCount, NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { smartRulesQuery, type SmartRule } from '../api/learningQueries';
import { Panel } from './Panel';
import { StoredStudy } from './StoredStudies';

const EMPTY_TEXT = 'No plan stored yet (the nightly service is paused until the owner starts it).';

/** A count the plan rows can give, or the word and reason when they cannot. Never a zero standing for "not known". */
function Count({ label, value, reason }: { label: string; value: number | null | undefined; reason: string }) {
  return (
    <Box component="div" sx={{ display: 'flex', gap: 0.75, alignItems: 'baseline', flexWrap: 'wrap' }}>
      <Typography component="dt" variant="body2" sx={{ fontWeight: 600 }}>
        {label}
      </Typography>
      <Typography component="dd" variant="body2" sx={{ m: 0 }}>
        {value === null || value === undefined ? (
          <NonValue state={{ kind: 'unavailable', reason }} />
        ) : (
          formatCount(value)
        )}
      </Typography>
    </Box>
  );
}

function RuleCard({ rule }: { rule: SmartRule }) {
  const reason = rule.notRecorded ?? 'not recorded';
  const empty = rule.state === 'empty';
  const stored = rule.id === 'windows' || rule.id === 'meta';
  return (
    <Panel title={rule.name}>
      <Chip
        size="small"
        variant="outlined"
        label={empty ? 'No plan stored' : rule.state === 'observed' ? 'Counted from stored plans' : 'Not recorded'}
        sx={{ mb: 1 }}
      />
      {empty ? (
        <NonValue state={{ kind: 'empty', reason: EMPTY_TEXT }} />
      ) : (
        <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
          <Count label="Ran on" value={rule.ranOn} reason="a plan row does not prove that a rule ran" />
          <Count label="Unavailable on" value={rule.unavailableOn} reason={reason} />
          {rule.id === 'correlation' && <Count label="Partly checked on" value={rule.partialOn} reason={reason} />}
          <Count label="Moved to reserve" value={rule.moved} reason={reason} />
        </Box>
      )}
      {rule.reasons.length > 0 && !empty && rule.moved !== null && (
        <ul aria-label={`${rule.name} reserve reasons`}>
          {rule.reasons.map((r) => (
            <li key={r.code}>
              <code>{r.code}</code>: {formatCount(r.count)}
            </li>
          ))}
        </ul>
      )}
      <Box sx={{ mt: 1 }}>
        {stored ? (
          <>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              Stored study
            </Typography>
            <StoredStudy rule={rule} />
          </>
        ) : (
          <>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              Outcome
            </Typography>
            <NonValue state={{ kind: 'not_evaluable', reason: rule.outcome.reason }} />
            <Typography variant="caption" color="text.secondary">
              Needs: <code>{rule.outcome.command}</code>
            </Typography>
          </>
        )}
      </Box>
    </Panel>
  );
}

/** What the stored plans say about each smart rule, and where they say nothing. Read only; no rule can be switched here. */
export function SmartRulesTab() {
  const query = useQuery(smartRulesQuery);
  if (query.isPending) return <LoadingState label="Loading smart-rule evidence…" />;
  if (query.isError)
    return (
      <Panel title="Smart rules">
        <ErrorState error={query.error} />
      </Panel>
    );
  const v = query.data;
  const empty = v.state === 'empty';
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">
        Counts of the codes the plan builder stored on plan items. Switching a rule off is an owner act with a password
        and is not done here; use the settings the owner already uses.
      </Typography>
      {v.rules.map((rule) => (
        <RuleCard key={rule.id} rule={rule} />
      ))}
      {v.flaggedPlans.length > 0 && (
        <Panel title="Flagged for review">
          <ul aria-label="Plans flagged for review">
            {v.flaggedPlans.map((f) => (
              <li key={f.planId}>
                Flagged for review: a rule removed half or more of its candidates on plan {formatCount(f.planId)} for{' '}
                {f.forDate} (<code>{f.code}</code>)
              </li>
            ))}
          </ul>
        </Panel>
      )}
      <EvidenceBlock
        label="Smart rules evidence"
        n={empty ? null : v.plansRead}
        range={v.from && v.to ? { from: v.from, to: v.to } : null}
        basis="unknown"
        basisText={v.source}
        limits={v.limits}
      />
    </Box>
  );
}

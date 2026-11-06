import { Box, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount, formatR, formatRatio, NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { cellsQuery } from '../api/learningQueries';
import { decayQuery, type DecayReport } from '../api/confidenceQueries';
import { DecaySeriesPanel } from './DecaySeriesPanel';
import { KillProposalsPanel } from './KillProposalsPanel';
import { KillRulesPanel } from './KillRulesPanel';
import { Panel } from './Panel';

function ReferencePanel({ report }: { report: DecayReport }) {
  const { reference, settings } = report;
  return (
    <Panel title="Reference and settings">
      <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
        {reference.state === 'available' ? (
          <Facts
            items={[
              { label: 'Reference trades (N_ref)', value: formatCount(reference.trades) },
              { label: 'Reference mean (mu_ref)', value: formatR(reference.meanR) },
              { label: 'Reference spread (sigma)', value: formatRatio(reference.sigma) },
              { label: 'Basis', value: reference.basis ?? 'not stated' },
            ]}
          />
        ) : (
          <Typography component="div">
            Reference: <NonValue state={{ kind: 'unavailable', reason: reference.reason ?? 'no reason given' }} />. A
            series without a reference is never judged.
          </Typography>
        )}
        <Facts
          items={[
            { label: 'Window', value: `${formatCount(settings.windowTrades)} trades, non-overlapping` },
            { label: 'Alert level', value: `${formatRatio(settings.sigmas)} standard errors below the reference mean` },
            { label: 'Weak-quarter floor', value: formatR(settings.quarterFloorR) },
            {
              label: 'Same-day correlation',
              value: `${formatRatio(settings.rho)}${settings.rhoIsPlaceholder ? ' (a placeholder until CF-01 estimates it)' : ''}`,
            },
            { label: 'Settings hash', value: settings.settingsHash },
          ]}
        />
      </Box>
    </Panel>
  );
}

function Report({ report }: { report: DecayReport }) {
  return (
    <>
      <Typography variant="body2" color="text.secondary">
        {report.line}
      </Typography>
      <ReferencePanel report={report} />
      <DecaySeriesPanel title="Edge series (primary)" series={report.edge} />
      <DecaySeriesPanel title="Behavioural paper series" series={report.behavioural} />
      <KillRulesPanel rules={report.kill} />
      <KillProposalsPanel strategy={report.strategy} />
      {report.statements.length > 0 && (
        <Box component="ul" aria-label="What to keep in mind" sx={{ m: 0, pl: 3 }}>
          {report.statements.map((sentence) => (
            <Typography key={sentence} component="li" variant="body2" color="text.secondary">
              {sentence}
            </Typography>
          ))}
        </Box>
      )}
    </>
  );
}

/**
 * Decay (CF-12 12c-2), read only: one strategy's windows against their alert level, twice in a row, CUSUM, the quarters,
 * the four kill rules with their numbers and the kill proposals. The strategy is in the address; the list is the strategies
 * of the stored cells. Nothing here pauses, applies or switches anything.
 */
export function DecayTab() {
  const url = useUrlState();
  const cells = useQuery(cellsQuery('all'));
  const requested = url.text('strategy');
  const listed = [...new Set((cells.data ?? []).map((cell) => cell.strategy))].sort((a, b) => a.localeCompare(b));
  const options = requested !== null && !listed.includes(requested) ? [requested, ...listed] : listed;
  const strategy = requested ?? listed[0] ?? null;
  const report = useQuery({ ...decayQuery(strategy ?? ''), enabled: strategy !== null });

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      <Panel
        title="Decay monitor"
        actions={
          options.length > 0 && (
            <TextField
              select
              size="small"
              label="Strategy"
              value={strategy ?? ''}
              onChange={(event) => {
                url.update({ strategy: event.target.value });
              }}
              slotProps={{ select: { native: true } }}
              sx={{ minWidth: 200 }}
            >
              {options.map((id) => (
                <option key={id} value={id}>
                  {id}
                </option>
              ))}
            </TextField>
          )
        }
      >
        {cells.isPending && <LoadingState label="Loading the strategies…" />}
        {cells.isError && <ErrorState error={cells.error} />}
        {cells.data && strategy === null && (
          <NonValue state={{ kind: 'empty', reason: 'No strategy to pick yet; run `xd learn cells`.' }} />
        )}
        {strategy !== null && report.isPending && <LoadingState label="Loading the decay report…" />}
        {report.isError && <ErrorState error={report.error} />}
        {report.data && (
          <Typography variant="body2" color="text.secondary">
            Read only: nothing here proposes, pauses or changes anything, and no order exists.
          </Typography>
        )}
      </Panel>
      {report.data && <Report report={report.data} />}
    </Box>
  );
}

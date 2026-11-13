import { Alert, Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, MetricValue, metricOf } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { scorecardQuery, type Scorecard } from '../api/readinessQueries';
import { checklistSize, formatScore, rowStatus } from '../lib/words';
import { ChecklistTable } from './ChecklistTable';
import { GatesPanel } from './GatesPanel';
import { Panel } from './Panel';
import { LabelChip } from './StatusChips';
import { SubScoresTable } from './SubScoresTable';

function Strings({ label, items }: { label: string; items: readonly string[] }) {
  return (
    <Box component="ul" aria-label={label} sx={{ m: 0, pl: 3 }}>
      {items.map((text) => (
        <Typography key={text} component="li" variant="body2">
          {text}
        </Typography>
      ))}
    </Box>
  );
}

function Headline({ card }: { card: Scorecard }) {
  const unavailable = card.rows.filter((row) => rowStatus(row.status) === 'unavailable').length;
  return (
    <>
      {(unavailable > 0 || card.waiting.length > 0) && (
        <Alert severity="info" sx={{ mb: 2 }}>
          {formatCount(unavailable)} of {checklistSize} checklist rows are not available. Each one counts as a fail,
          never as a pass and never as a measured 0, so this score is a floor and Gate R cannot pass until the inputs
          below exist. That is the designed outcome while producers are missing, not a fault of the screen.
        </Alert>
      )}
      <Facts
        items={[
          {
            label: 'Score',
            value: (
              <>
                <MetricValue state={metricOf(card.score, 'The score is not a number.')} format={formatScore} /> of 100{' '}
                <LabelChip code={card.label} text={card.labelText} />
              </>
            ),
          },
          {
            label: 'Minimum before hard fails',
            value: (
              <MetricValue state={metricOf(card.rawMinimum, 'The minimum is not a number.')} format={formatScore} />
            ),
          },
          { label: 'Weakest link', value: card.weakestSubScore ?? 'None' },
          { label: 'What would move it', value: card.whatWouldMoveIt ?? 'Nothing is computed' },
          {
            label: 'Hard fails',
            value: card.hardFails.length === 0 ? 'None' : <Strings label="Hard fails" items={card.hardFails} />,
          },
          {
            label: 'Left out (not applicable)',
            value: card.notApplicable.length === 0 ? 'Nothing' : card.notApplicable.join('; '),
          },
          { label: 'Rows passed', value: `${formatCount(card.rowsPassed)} of ${String(checklistSize)}` },
          {
            label: 'Enforce',
            value: card.enforce
              ? 'ON: a failed gate refuses the move'
              : 'OFF: a failed gate warns and records, the move goes ahead',
          },
          { label: 'Settings hash', value: card.settingsHash },
        ]}
      />
    </>
  );
}

function History({ card }: { card: Scorecard }) {
  return (
    <Panel title="Stored snapshots">
      <DataTable
        label="Daily snapshots, newest first"
        rows={card.history}
        rowKey={(row) => String(row.id)}
        status={
          card.history.length === 0
            ? {
                kind: 'empty',
                reason:
                  'No daily snapshot is stored yet. The readiness-snapshot service runs daily after the evening runs (enabled by default); a snapshot appears after its first run.',
              }
            : undefined
        }
        columns={[
          { kind: 'text', id: 'id', header: 'Snapshot', render: (row) => `#${String(row.id)}` },
          { kind: 'text', id: 'date', header: 'Date (India)', render: (row) => row.date },
          {
            kind: 'metric',
            id: 'score',
            header: 'Score (of 100)',
            metric: (row) => metricOf(row.score, 'The score is not a number.'),
            format: formatScore,
          },
          { kind: 'text', id: 'label', header: 'Label', render: (row) => row.label.replaceAll('_', ' ') },
          { kind: 'text', id: 'hash', header: 'Settings hash', render: (row) => row.settingsHash },
        ]}
      />
    </Panel>
  );
}

/** One strategy version's scorecard. A strategy the server refuses (404, 400) shows the server's words. */
export function ScorecardDetail({ strategy, version }: { strategy: string; version: number }) {
  const query = useQuery(scorecardQuery(strategy, version));
  if (query.isPending) return <LoadingState label="Loading the scorecard…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const card = query.data;
  return (
    <>
      <Panel title={`Scorecard: ${card.strategy}${card.version === null ? '' : ` v${String(card.version)}`}`}>
        <Headline card={card} />
      </Panel>
      <Panel title="Sub-scores">
        <SubScoresTable subScores={card.subScores} />
      </Panel>
      <Panel title="Edge checklist">
        <ChecklistTable rows={card.rows} />
      </Panel>
      <GatesPanel gateR={card.gateR} gateS={card.gateS} />
      <Panel title="Waiting for">
        {card.waiting.length === 0 ? (
          <Typography>Nothing: every input exists.</Typography>
        ) : (
          <>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              These producers have not delivered an input. Each unavailable input counts as a fail until it does.
            </Typography>
            <Strings label="Producers this card is waiting for" items={card.waiting} />
          </>
        )}
      </Panel>
      <History card={card} />
      {card.notes.length > 0 && <Strings label="What to keep in mind" items={card.notes} />}
    </>
  );
}

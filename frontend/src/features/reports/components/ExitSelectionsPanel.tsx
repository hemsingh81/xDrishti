import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, formatRatio, NonValue } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import { exitSelectionsQuery, type ExitSelectionItem } from '../api/reportQueries';
import { metricFromApi } from '../lib/metricFromApi';
import { QueryPanel } from './Panel';

const verdict = (s: ExitSelectionItem) =>
  s.stable === null
    ? `No stability statement: ${s.stabilityReason}`
    : `${s.stable ? 'Stable' : 'Unstable'}: ${s.stabilityReason}`;

function Selection({ s }: { s: ExitSelectionItem }) {
  const cell = `${s.strategyId} ${s.timeframe} ${s.side} ${s.style} ${s.regime}`;
  if (s.unreadable !== null) {
    return (
      <Box component="section" aria-label={`Exit choice for ${cell}`} sx={{ display: 'grid', gap: 1 }}>
        <Typography variant="h3" component="h3">
          {cell}
        </Typography>
        <NonValue state={{ kind: 'unavailable', reason: s.unreadable }} />
      </Box>
    );
  }
  return (
    <Box component="section" aria-label={`Exit choice for ${cell}`} sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="h3" component="h3">
        {cell}
      </Typography>
      <Typography variant="body1" sx={{ fontWeight: 600 }}>
        {s.chosenCandidateId
          ? `Walk-forward choice: ${s.chosenCandidateId} (default: ${s.defaultCandidateId}).`
          : `No choice: ${s.noChoiceReason ?? 'none stood'}. The default ${s.defaultCandidateId} is used.`}
      </Typography>
      <Typography variant="body2">
        {verdict(s)}
        {s.chancePass === null
          ? ''
          : `. Random winners would pass the agreement rule with probability ${formatRatio(s.chancePass)}.`}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {`${formatCount(s.agreeingFolds)} of ${formatCount(s.usableFolds)} usable folds agree with the modal winner; ${formatCount(s.positiveFolds)} of the agreeing are positive out of sample; ${formatCount(s.lonePeaks)} lone peaks.`}
      </Typography>
      <DataTable
        label={`Pooled out-of-sample figures, ${cell}`}
        rows={s.pooled}
        rowKey={(p) => p.name}
        columns={[
          { kind: 'text', id: 'name', header: 'Exit', render: (p) => p.name },
          { kind: 'text', id: 'n', header: 'n', align: 'right', render: (p) => formatCount(p.trades) },
          {
            kind: 'metric',
            id: 'mean',
            header: `Mean (${s.unit})`,
            metric: (p) => metricFromApi(p.meanR, p.trades, s.minTrades),
            format: formatRatio,
            tone: 'gain-loss',
          },
          {
            kind: 'metric',
            id: 'score',
            header: 'Score',
            metric: (p) => metricFromApi(p.score, p.trades, s.minTrades),
            format: formatRatio,
            tone: 'gain-loss',
          },
        ]}
      />
      <DataTable
        label={`Folds, ${cell}`}
        rows={s.folds}
        rowKey={(f) => String(f.index)}
        columns={[
          { kind: 'text', id: 'fold', header: 'Fold', render: (f) => formatCount(f.index) },
          {
            kind: 'text',
            id: 'window',
            header: 'Test window',
            render: (f) => `${formatDate(f.testStart)} to ${formatDate(f.testEnd)}`,
          },
          { kind: 'text', id: 'winner', header: 'Winner', render: (f) => f.winner ?? 'None qualified' },
          { kind: 'text', id: 'n', header: 'n', align: 'right', render: (f) => formatCount(f.winnerTestTrades) },
          {
            kind: 'metric',
            id: 'score',
            header: 'Out-of-sample score',
            metric: (f) => metricFromApi(f.winnerTestScore, f.winnerTestTrades, s.minTrades),
            format: formatRatio,
            tone: 'gain-loss',
          },
          {
            kind: 'text',
            id: 'thin',
            header: 'Counts toward the verdict',
            render: (f) => (f.thin ? 'No (thin)' : 'Yes'),
          },
        ]}
      />
      <Typography variant="body2" color="text.secondary">
        {`A fold counts toward the verdict by the record's own rule (a winner with enough test trades). A figure in these tables needs at least ${formatCount(s.minTrades)} trades behind it, so a fold can count and still show an insufficient score.`}
      </Typography>
      {s.banner.map((line) => (
        <Typography key={line} variant="body2" color="text.secondary">
          {line}
        </Typography>
      ))}
    </Box>
  );
}

/** The strongest "which exit" evidence: the stored walk-forward choices (ADR 0054), read from their records. Descriptive; nothing is applied. */
export function ExitSelectionsPanel() {
  const result = useQuery(exitSelectionsQuery);
  return (
    <QueryPanel title="Out-of-sample exit choices" query={result}>
      {(view) => (
        <Box sx={{ display: 'grid', gap: 2 }}>
          {view.selections.length === 0 ? (
            <NonValue
              state={{
                kind: 'empty',
                reason: 'No walk-forward exit choice is stored. Make one with xd simulate --select-exit --store.',
              }}
            />
          ) : (
            view.selections.map((s) => <Selection key={s.selectionKey} s={s} />)
          )}
          {view.limits.map((line) => (
            <Typography key={line} variant="body2" color="text.secondary">
              {line}
            </Typography>
          ))}
        </Box>
      )}
    </QueryPanel>
  );
}

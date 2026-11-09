import { Alert, Box, Chip, Typography } from '@mui/material';
import { EvidenceBlock, NonValue, formatR, formatRate } from '@/shared/analytics';
import type { ExtensionEvaluation } from '../api/extensionQueries';
import { boundsOf, windowOf, type BoundLine } from '../lib/extensions';
import { FilterSharesTable } from './FilterSharesTable';

const syntheticNote =
  'The fundamentals come from the synthetic results parser (xbrl-1-synthetic), not checked against real filings.';

function Bounds({ e }: { e: ExtensionEvaluation }) {
  const lines = boundsOf(e);
  const fmt = (b: BoundLine) =>
    b.value === null ? 'unavailable' : b.unit === 'share' ? formatRate(b.value) : formatR(b.value);
  return (
    <Typography variant="body2">
      {lines.map((b) => `${b.label} ${fmt(b)}`).join('; ')}
      {lines.length > 0 ? '; ' : ''}
      margin widened by the ledger count ({e.trials} trials, factor {e.factor.toFixed(2)}).
    </Typography>
  );
}

/** One stored record: the verdict word, N, what is missing (as text), the bounds, the evidence and, for the filter, the shares. */
export function ExtensionCard({ family, e }: { family: string; e: ExtensionEvaluation }) {
  const n = windowOf(e);
  const word = e.verdict.replaceAll('_', ' ');
  return (
    <Box
      component="article"
      aria-label={`${family} ${e.holdOutRead ? 'hold-out' : 'development'}${n === null ? '' : ` N=${String(n)}`}`}
      sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, display: 'grid', gap: 1 }}
    >
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Chip size="small" variant="outlined" label={`Verdict: ${word}`} />
        <Chip size="small" variant="outlined" label={e.holdOutRead ? 'Hold-out reading' : 'Development'} />
        {n !== null && <Chip size="small" variant="outlined" label={`N=${String(n)}`} />}
        <Typography variant="caption" color="text.secondary">
          folds {e.foldsEvaluable} of {e.foldsTotal} judged · {e.foldUnit}
        </Typography>
      </Box>
      {e.missing.length > 0 && (
        <Box component="ul" aria-label="Missing" sx={{ m: 0, pl: 2.5 }}>
          {e.missing.map((m) => (
            <li key={m}>
              <Typography variant="body2">{m}</Typography>
            </li>
          ))}
        </Box>
      )}
      {(e.verdict === 'KEEP' || e.verdict === 'DROP') && <Bounds e={e} />}
      {e.verdict === 'INCONCLUSIVE' && (
        <NonValue state={{ kind: 'not_evaluable', reason: 'the bound did not clear the margin either way' }} />
      )}
      {family === 'filter:results' && !e.holdOutRead && <FilterSharesTable evaluation={e} />}
      {family === 'ext:fu-1' && <Alert severity="info">{syntheticNote}</Alert>}
      <EvidenceBlock
        label={`${family} evidence`}
        n={e.rows}
        range={null}
        basis="unknown"
        basisText={`Protocol ${e.protocol}, base ${e.baseVersion}`}
        limits={e.limits}
      />
    </Box>
  );
}

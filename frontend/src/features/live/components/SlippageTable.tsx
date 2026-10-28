import { Stack, Typography } from '@mui/material';
import type { LiveSlippageView } from '../api/executionQueries';
import { figure, interval, referenceWords, slippageSentence } from '../lib/executionLabels';
import { ExecutionStateChip } from './ExecutionStateChip';

/**
 * Slippage per liquidity bucket (spec P11-02): the bucket edges and the reference kinds are printed above the table; a row under the minimum
 * shows its n and the words, never a mean. The two reference kinds are two rows. A proposal sentence is shown as text and read by no one.
 */
export function SlippageTable({ view }: { view: LiveSlippageView }) {
  const proposals = view.rows.filter((r) => r.proposal !== null);
  return (
    <Stack component="section" aria-label="Slippage" sx={{ gap: 1 }}>
      <Typography variant="h3" component="h3">
        Slippage per liquidity bucket
      </Typography>
      <Typography variant="body2">
        {`Bucket edges (average daily turnover, crore): ${view.evidence.bucketEdgesCr.join(', ')}. L1 is the most liquid. Reference kinds: ${view.evidence.referenceKinds.map((k) => referenceWords[k] ?? k).join(', ')}, never pooled.`}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {slippageSentence}
      </Typography>
      <table aria-label="Slippage by bucket">
        <thead>
          <tr>
            <th scope="col">Bucket</th>
            <th scope="col">Side</th>
            <th scope="col">Style</th>
            <th scope="col">Reference</th>
            <th scope="col">n</th>
            <th scope="col">Mean (bp)</th>
            <th scope="col">Median (bp)</th>
            <th scope="col">Mean R</th>
            <th scope="col">State</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((r) => (
            <tr key={`${r.bucket}|${r.side}|${r.style}|${r.referenceKind}`}>
              <th scope="row">{r.bucket}</th>
              <td>{r.side}</td>
              <td>{r.style}</td>
              <td>{referenceWords[r.referenceKind] ?? r.referenceKind}</td>
              <td>{r.n}</td>
              {r.state === 'value' ? (
                <>
                  <td>{`${figure(r.mean, 2)} (${interval(r.meanLo, r.meanHi, 2)})`}</td>
                  <td>{`${figure(r.median, 2)} (${interval(r.medianLo, r.medianHi, 2)})`}</td>
                  <td>{figure(r.meanR, 4)}</td>
                </>
              ) : (
                <td colSpan={3}>{r.mean.reason ?? `n = ${String(r.n)}`}</td>
              )}
              <td>
                <ExecutionStateChip state={r.state} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {proposals.length > 0 && (
        <section aria-label="Proposals">
          <Typography variant="body2">Proposals (stored beside the row; nothing reads them automatically):</Typography>
          <ul>
            {proposals.map((r) => (
              <li key={`${r.bucket}|${r.side}|${r.style}|${r.referenceKind}`}>
                <Typography variant="body2">{r.proposal}</Typography>
              </li>
            ))}
          </ul>
        </section>
      )}
    </Stack>
  );
}

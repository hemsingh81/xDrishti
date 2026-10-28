import { Typography } from '@mui/material';
import type { ReportEvidence as Evidence } from '../api/reportQueries';
import { reasonWords } from '../lib/reportLabels';

/** What the figures rest on: basis, n, sessions, reference kind, paper account and epoch, the pair rule, the exclusions and the limits. */
export function ReportEvidence({ evidence }: { evidence: Evidence }) {
  const range =
    evidence.firstDate !== null && evidence.lastDate !== null
      ? `${evidence.firstDate} to ${evidence.lastDate}`
      : 'no session yet';
  const paper =
    evidence.paperAccountId !== null
      ? `paper account ${evidence.paperAccountId}, epoch ${String(evidence.paperEpochId ?? 'none')}`
      : 'no paper account';
  return (
    <section aria-label="Evidence">
      <Typography variant="h3" component="h3">
        Evidence
      </Typography>
      <Typography>{`Basis: ${evidence.basis}. n = ${String(evidence.n)}; sessions: ${range}; ${paper}.`}</Typography>
      {evidence.referenceKinds.length > 0 && (
        <Typography variant="body2">{`Slippage reference: ${evidence.referenceKinds.join(', ')}.`}</Typography>
      )}
      <Typography variant="body2">{evidence.pairRule}</Typography>
      {evidence.exclusions.length > 0 && (
        <Typography variant="body2">
          {`Left out: ${evidence.exclusions.map((e) => `${reasonWords[e.reason] ?? e.reason} ${String(e.count)}`).join('; ')}.`}
        </Typography>
      )}
      <ul>
        {evidence.limits.map((limit) => (
          <li key={limit}>
            <Typography variant="body2">{limit}</Typography>
          </li>
        ))}
      </ul>
    </section>
  );
}

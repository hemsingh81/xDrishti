import { Typography } from '@mui/material';
import type { EvidenceView } from '../api/executionQueries';

/** What the figures rest on: the basis, how many entries, over which sessions, and the stated limits. */
export function ExecutionEvidence({ evidence }: { evidence: EvidenceView }) {
  const range =
    evidence.firstDate !== null && evidence.lastDate !== null
      ? `${evidence.firstDate} to ${evidence.lastDate}`
      : 'no session yet';
  return (
    <section aria-label="Evidence">
      <Typography variant="h3" component="h3">
        Evidence
      </Typography>
      <Typography>{`Basis: ${evidence.basis}. n = ${String(evidence.n)}; sessions: ${range}.`}</Typography>
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

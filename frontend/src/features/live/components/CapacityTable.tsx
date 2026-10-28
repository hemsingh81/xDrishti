import { Stack, Typography } from '@mui/material';
import type { LiveCapacityView } from '../api/executionQueries';
import { figure, interval } from '../lib/executionLabels';
import { ExecutionStateChip } from './ExecutionStateChip';

/** The capacity score (spec P11-02): the share of orders filled in full, for the account and each bucket. Display only; selection does not read it. */
export function CapacityTable({ view }: { view: LiveCapacityView }) {
  return (
    <Stack component="section" aria-label="Capacity" sx={{ gap: 1 }}>
      <Typography variant="h3" component="h3">
        Capacity: share of orders filled in full
      </Typography>
      <Typography variant="body2" color="text.secondary">
        Full, partial, rejected and queue-miss orders are counted. A price miss (the market never reached your limit)
        and an unclassified order are shown apart and not counted.
      </Typography>
      <table aria-label="Capacity by scope">
        <thead>
          <tr>
            <th scope="col">Scope</th>
            <th scope="col">Counted</th>
            <th scope="col">Full</th>
            <th scope="col">Partial</th>
            <th scope="col">Rejected</th>
            <th scope="col">Queue miss</th>
            <th scope="col">Price miss (not counted)</th>
            <th scope="col">Unclassified (not counted)</th>
            <th scope="col">Share filled in full</th>
            <th scope="col">State</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((r) => (
            <tr key={`${r.scope}|${r.key}`}>
              <th scope="row">{r.scope === 'account' ? 'Account' : r.key}</th>
              <td>{r.nOrders}</td>
              <td>{r.nFull}</td>
              <td>{r.nPartial}</td>
              <td>{r.nRejected}</td>
              <td>{r.nQueueMiss}</td>
              <td>{r.nPriceMiss}</td>
              <td>{r.nUnclassified}</td>
              <td>
                {r.state === 'value'
                  ? `${figure(r.score, 4)} (${interval(r.lo, r.hi, 3)})`
                  : (r.score.reason ?? figure(r.score, 4))}
              </td>
              <td>
                <ExecutionStateChip state={r.state} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Stack>
  );
}

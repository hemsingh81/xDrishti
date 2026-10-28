import { Typography } from '@mui/material';
import type { ExecutionGapView, GapGroup } from '../api/reportQueries';
import { reasonWords, r } from '../lib/reportLabels';

function Group({ group }: { group: GapGroup }) {
  return (
    <section aria-label={`Gap ${group.key}`}>
      <Typography variant="h3" component="h3">{`${group.key} (n = ${String(group.n)}, ${group.basis})`}</Typography>
      <table aria-label={`Waterfall ${group.key}`}>
        <tbody>
          {group.rows.map((row) => (
            <tr key={row.label}>
              <th scope="row">{row.label}</th>
              <td>{r(row.r, 4)}</td>
              <td>{row.r.state === 'value' ? '' : (row.r.reason ?? '')}</td>
            </tr>
          ))}
          <tr>
            <th scope="row">Mean per ticket</th>
            <td>{r(group.perTicketMean, 4)}</td>
            <td>{group.perTicketMean.state === 'value' ? '' : (group.perTicketMean.reason ?? '')}</td>
          </tr>
        </tbody>
      </table>
      {group.excluded > 0 && <Typography variant="body2">{`${String(group.excluded)} tickets left out.`}</Typography>}
    </section>
  );
}

/** The execution gap by strategy or month: plan R to actual R, each row a value only when its input exists. */
export function ReportGap({ view }: { view: ExecutionGapView }) {
  return (
    <>
      {view.groups.length === 0 ? (
        <Typography>No graded ticket in the range yet.</Typography>
      ) : (
        [...view.groups, view.total].map((g) => <Group key={g.key} group={g} />)
      )}
      {view.excluded.length > 0 && (
        <ul aria-label="Left out of the gap">
          {view.excluded.map((e) => (
            <li key={e.planItemId}>
              <Typography variant="body2">{`${e.symbol} ${e.date}, ticket ${String(e.planItemId)}: ${reasonWords[e.reason] ?? e.reason}`}</Typography>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
